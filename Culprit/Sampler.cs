using Culprit.Core;
using Culprit.Core.Alerting;
using Culprit.Core.Attribution;
using Culprit.Core.Detection;
using Culprit.Core.Models;
using Culprit.Probes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace Culprit;

/// <summary>
/// Runs once per framework tick: rebuilds the roster if it went dirty, decides suppression,
/// reads every available probe, feeds the engine, stores incidents, asks the alert policy, and
/// shows toasts. Everything here is on the framework thread except LogProbe's enqueue side.
/// Suppression is blocked while zoning or in a cutscene, for 5 seconds after that clears, and
/// for 5 seconds after any plugin loads or unloads.
/// </summary>
internal sealed class Sampler : IDisposable
{
    private const int MaxUnattributedHandlers = 200;

    private static readonly ConditionFlag[] SuppressingFlags =
    [
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
    ];

    private readonly Configuration config;
    private readonly Roster roster;
    private readonly DrawStatsProbe drawProbe;
    private readonly UpdateStatsProbe updateProbe;
    private readonly LogProbe logProbe;
    private readonly FrameProbe frameProbe = new();
    private readonly SuppressionTracker suppression = new(TimeSpan.FromSeconds(5));
    private readonly Notifier notifier;
    private readonly Dictionary<string, (double Draw, double Update)> frameTimings = new(StringComparer.Ordinal);
    private readonly HashSet<string> unattributedHandlers = new(StringComparer.Ordinal);

    public Sampler(Configuration config, Roster roster, DrawStatsProbe drawProbe, UpdateStatsProbe updateProbe, LogProbe logProbe, Notifier notifier)
    {
        this.config = config;
        this.roster = roster;
        this.drawProbe = drawProbe;
        this.updateProbe = updateProbe;
        this.logProbe = logProbe;
        this.notifier = notifier;

        this.Engine = new Engine(config.ToThresholds(), "Culprit");
        this.Policy = new AlertPolicy(config.ToAlertSettings());
        this.ApplyConfig();

        this.drawProbe.Refresh();
        Services.Framework.Update += this.OnUpdate;
    }

    public Engine Engine { get; }

    public IncidentStore Store { get; } = new();

    public AlertPolicy Policy { get; }

    public IReadOnlyList<ProbeStatus> ProbeStatuses =>
        [this.frameProbe.Status, this.drawProbe.Status, this.updateProbe.Status, this.logProbe.Status, this.roster.AttributionStatus, this.roster.HandlerMapStatus];

    public bool ReducedMode => this.ProbeStatuses.Any(s => !s.Available);

    /// <summary>Update-handler keys that could not be attributed to a plugin, capped at 200.</summary>
    public IReadOnlyCollection<string> UnattributedHandlers => this.unattributedHandlers;

    /// <summary>Number of generation-2 GC collections since the process started.</summary>
    public int GcGen2Collections => this.frameProbe.Gen2Collections;

    /// <summary>Total milliseconds of GC pause since the process started.</summary>
    public double GcTotalPauseMs => this.frameProbe.TotalPauseMs;

    /// <summary>Short warnings for probes whose Dalamud-side tracking switch is currently off.</summary>
    public IReadOnlyList<string> TrackingWarnings
    {
        get
        {
            var warnings = new List<string>();
            if (!this.drawProbe.TrackingEnabled)
            {
                warnings.Add("Draw time tracking is off in /xlstats");
            }

            if (!this.updateProbe.TrackingEnabled)
            {
                warnings.Add("Framework update tracking is off in /xlstats");
            }

            return warnings;
        }
    }

    public bool Paused
    {
        get => this.Engine.Paused;
        set => this.Engine.Paused = value;
    }

    /// <summary>Re-reads thresholds, cooldown, mute and ignore list from the configuration.</summary>
    public void ApplyConfig()
    {
        this.Engine.Thresholds = this.config.ToThresholds();
        this.Policy.Settings = this.config.ToAlertSettings();
        this.Engine.Ignored.Clear();
        foreach (var name in this.config.IgnoredPlugins)
        {
            this.Engine.Ignored.Add(name);
        }
    }

    public void Reset()
    {
        this.Engine.Reset();
        this.Store.Reset();
        this.Policy.Reset();
        this.unattributedHandlers.Clear();
    }

    public void Dispose()
    {
        Services.Framework.Update -= this.OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            this.Tick();
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Culprit sampler tick failed");
        }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var frame = this.frameProbe.Tick();

        if (this.roster.ConsumeDirty())
        {
            this.suppression.MarkPluginChange(now);
            this.roster.Rebuild();
            this.drawProbe.Refresh();
        }

        if (this.roster.ShouldRefreshHandlerMap(now))
        {
            this.roster.RefreshHandlerMap();
        }

        this.Engine.Suppressed = this.suppression.Evaluate(SuppressingFlags.Any(f => Services.Condition[f]), now);

        this.DrainErrors();
        this.DrainHitches();

        if (frame.FrameMs is null)
        {
            return;
        }

        this.frameTimings.Clear();

        foreach (var reading in this.drawProbe.Read())
        {
            this.frameTimings[reading.InternalName] = (reading.LastMs, 0);
        }

        foreach (var reading in this.updateProbe.Read())
        {
            if (this.roster.Resolver.Resolve(reading.HandlerKey) is not { } name)
            {
                // While the background index is still filling in, everything looks unattributed;
                // recording those would spend the cap on keys that are only temporarily unknown.
                if (!this.roster.Indexing && this.unattributedHandlers.Count < MaxUnattributedHandlers)
                {
                    this.unattributedHandlers.Add(reading.HandlerKey);
                }

                continue;
            }

            var current = this.frameTimings.GetValueOrDefault(name);
            this.frameTimings[name] = (current.Draw, current.Update + reading.Ms);
        }

        var plugins = this.frameTimings.Select(kv => new PluginTiming(kv.Key, kv.Value.Draw, kv.Value.Update)).ToList();
        foreach (var incident in this.Engine.Process(new FrameSample(now, frame.FrameMs.Value, plugins, frame.GcPauseMs)))
        {
            this.Handle(incident, now);
        }
    }

    private void DrainErrors()
    {
        while (this.logProbe.TryDequeueError(out var raw))
        {
            var name = raw.PluginName ?? raw.Frames.Select(this.roster.InternalNameForAssembly).FirstOrDefault(n => n is not null);
            if (name is null || this.Engine.IsIgnored(name) || this.Engine.Paused)
            {
                continue;
            }

            this.Store.RecordError(name);
            if (this.Engine.Process(new ErrorEvent(raw.Timestamp, name, raw.Message)) is { } incident)
            {
                this.Handle(incident, raw.Timestamp);
            }
        }
    }

    /// <summary>Dalamud's own hitch warnings only matter when Culprit's own timing for that source is unavailable.</summary>
    private void DrainHitches()
    {
        while (this.logProbe.TryDequeueHitch(out var raw))
        {
            var parsed = HitchNameParser.Parse(raw.Name);
            HitchEvent? hitch = parsed.Kind switch
            {
                HitchKind.Draw when !this.drawProbe.Status.Available =>
                    new HitchEvent(raw.Timestamp, this.roster.InternalNameForDisplay(parsed.Target), raw.Ms, raw.LimitMs, "Draw"),
                HitchKind.Update when !this.updateProbe.Status.Available =>
                    new HitchEvent(raw.Timestamp, this.roster.TypeIndex.ResolveHandlerKey(parsed.Target), raw.Ms, raw.LimitMs, "Update"),
                _ => null,
            };

            if (hitch is not null && this.Engine.Process(hitch) is { } incident)
            {
                this.Handle(incident, raw.Timestamp);
            }
        }
    }

    private void Handle(Incident incident, DateTime now)
    {
        this.Store.Add(incident);
        var decision = this.Policy.Decide(incident, now);
        incident.Notified = decision.Notify;

        if (decision.Notify)
        {
            var title = ToastFormatter.Title(incident, incident.InternalName is { } n ? this.roster.DisplayName(n) : null);
            var body = ToastFormatter.Body(incident, this.Engine.Thresholds, decision.SuppressedSinceLast);
            this.notifier.Show(incident, title, body);
        }

        if (this.config.DebugLog)
        {
            Services.Log.Debug($"[Incident] {incident.Rule} {incident.InternalName ?? "(none)"} {incident.Value:0.0} > {incident.Limit:0.0} {incident.Detail} notified={decision.Notify}");
        }
    }
}
