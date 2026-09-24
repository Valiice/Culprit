using System.Reflection;
using Culprit.Core.Sampling;

namespace Culprit.Probes;

public sealed record UpdateReading(string HandlerKey, double Ms);

/// <summary>
/// Reads Dalamud's Framework.StatsHistory: one list of ms samples per update handler, keyed
/// "Type::Method". Framework thread only. Turns on Framework.StatsEnabled and restores it on
/// dispose. Trims long lists so the count never reaches Dalamud's cap (see FreshnessTracker).
/// </summary>
internal sealed class UpdateStatsProbe : IDisposable
{
    private const string Name = "Update stats";
    private const int MaxFailures = 3;

    private readonly PropertyInfo? statsEnabled;
    private readonly Dictionary<string, List<double>>? history;
    private readonly bool previousEnabled;
    private readonly FreshnessTracker tracker = new();
    private int failures;

    public UpdateStatsProbe(DalamudInternals internals)
    {
        try
        {
            var framework = internals.GetType("Dalamud.Game.Framework");
            this.statsEnabled = DalamudInternals.Property(framework, "StatsEnabled");
            this.history = DalamudInternals.Property(framework, "StatsHistory").GetValue(null) as Dictionary<string, List<double>>
                           ?? throw new InvalidCastException("Framework.StatsHistory is not Dictionary<string, List<double>>");
            this.previousEnabled = (bool)(this.statsEnabled.GetValue(null) ?? false);
            this.statsEnabled.SetValue(null, true);
            this.Status = ProbeStatus.Ok(Name);
        }
        catch (Exception e)
        {
            this.Status = ProbeStatus.Failed(Name, e);
        }
    }

    public ProbeStatus Status { get; private set; }

    /// <summary>New samples since the last call, one per handler key that was invoked since then.</summary>
    public IReadOnlyList<UpdateReading> Read()
    {
        if (this.history is not { } history || !this.Status.Available)
        {
            return [];
        }

        var result = new List<UpdateReading>();
        try
        {
            foreach (var (key, list) in history)
            {
                var count = list.Count;
                if (count == 0)
                {
                    continue;
                }

                if (this.tracker.IsNew(key, count))
                {
                    result.Add(new UpdateReading(key, list[count - 1]));
                }

                if (FreshnessTracker.ShouldTrim(count))
                {
                    list.RemoveRange(0, count - FreshnessTracker.TrimTo);
                    count = FreshnessTracker.TrimTo;
                }

                this.tracker.Record(key, count);
            }

            this.failures = 0;
        }
        catch (Exception e)
        {
            if (++this.failures >= MaxFailures)
            {
                this.Status = ProbeStatus.Failed(Name, e);
                Services.Log.Warning(e, "Culprit update stats probe disabled after repeated failures");
            }
        }

        return result;
    }

    public void Dispose()
    {
        try
        {
            this.statsEnabled?.SetValue(null, this.previousEnabled);
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit could not restore Framework.StatsEnabled");
        }
    }
}
