using System.Globalization;
using Culprit.Core.Models;

namespace Culprit.Core.Detection;

/// <summary>
/// Turns frame samples and error events into incidents. Pure: no clock, no I/O. The caller
/// sets Suppressed (zoning, cutscene, grace period) and Paused (user command) before feeding it.
/// Frames that crossed the stall limit are reported as stalls and left out of the sustained
/// average, so one huge frame cannot echo as a Sustained incident. A frame whose GC pause
/// accounts for at least half of its total time is treated as GC-heavy: every plugin's timing
/// for that frame is contaminated (the GC pause could have landed in any handler), so it still
/// updates Last/Longest but is excluded from the sustained average and never reported as a
/// Stall or Sustained incident; a long GC-heavy frame is instead reported as one UnattributedStall
/// whose Detail names the GC pause.
/// </summary>
public sealed class Engine
{
    private readonly Dictionary<string, PluginWindow> windows = new(StringComparer.Ordinal);
    private readonly string selfInternalName;

    public Engine(Thresholds thresholds, string selfInternalName)
    {
        this.Thresholds = thresholds;
        this.selfInternalName = selfInternalName;
    }

    public Thresholds Thresholds { get; set; }

    /// <summary>User paused detection. Nothing is recorded or emitted.</summary>
    public bool Paused { get; set; }

    /// <summary>Environment says now is not a fair time to judge (zoning, cutscene, grace).</summary>
    public bool Suppressed { get; set; }

    /// <summary>User ignore list. Self is ignored separately and cannot be removed.</summary>
    public HashSet<string> Ignored { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> KnownPlugins => this.windows.Keys;

    public bool IsIgnored(string internalName) =>
        string.Equals(internalName, this.selfInternalName, StringComparison.OrdinalIgnoreCase)
        || this.Ignored.Contains(internalName);

    /// <summary>
    /// While Suppressed, nothing is recorded or emitted. While Paused, sampling still runs (so the
    /// Live tab keeps updating) but no incidents are emitted.
    /// </summary>
    public IReadOnlyList<Incident> Process(FrameSample sample)
    {
        if (this.Suppressed)
        {
            return [];
        }

        var t = this.Thresholds;
        var incidents = new List<Incident>();
        var anyStall = false;
        var gcHeavy = sample.GcPauseMs > 0 && sample.GcPauseMs >= sample.FrameMs * 0.5;

        foreach (var timing in sample.Plugins)
        {
            if (this.IsIgnored(timing.InternalName))
            {
                continue;
            }

            var window = this.Window(timing.InternalName);

            if (gcHeavy)
            {
                // The GC pause could have landed in any handler, so this frame's timings are
                // contaminated: still track Last/Longest, but keep them out of the sustained
                // average and never turn them into a Stall or Sustained incident.
                window.AddTiming(sample.Timestamp, timing.DrawMs, timing.UpdateMs, includeInAverage: false);
                window.CompleteWindowIfDue(sample.Timestamp, t.SustainedWindowSeconds);
                continue;
            }

            var isStall = timing.DrawMs > t.StallMs || timing.UpdateMs > t.StallMs;
            window.AddTiming(sample.Timestamp, timing.DrawMs, timing.UpdateMs, includeInAverage: !isStall);
            var due = window.CompleteWindowIfDue(sample.Timestamp, t.SustainedWindowSeconds);

            if (this.Paused)
            {
                continue;
            }

            if (timing.DrawMs > t.StallMs)
            {
                anyStall = true;
                if (window.ShouldEmitStall("Draw", sample.Timestamp))
                {
                    incidents.Add(new Incident(sample.Timestamp, timing.InternalName, IncidentRule.Stall, timing.DrawMs, t.StallMs, "Draw"));
                }
            }

            if (timing.UpdateMs > t.StallMs)
            {
                anyStall = true;
                if (window.ShouldEmitStall("Update", sample.Timestamp))
                {
                    incidents.Add(new Incident(sample.Timestamp, timing.InternalName, IncidentRule.Stall, timing.UpdateMs, t.StallMs, "Update"));
                }
            }

            if (due is { } average && average > t.SustainedMs)
            {
                incidents.Add(new Incident(sample.Timestamp, timing.InternalName, IncidentRule.Sustained, average, t.SustainedMs, $"{t.SustainedWindowSeconds:0} s window"));
            }
        }

        if (this.Paused)
        {
            return [];
        }

        if (gcHeavy)
        {
            if (sample.FrameMs > t.UnattributedMs)
            {
                var detail = string.Format(CultureInfo.InvariantCulture, "GC pause {0:0} ms", sample.GcPauseMs);
                incidents.Add(new Incident(sample.Timestamp, null, IncidentRule.UnattributedStall, sample.FrameMs, t.UnattributedMs, detail));
            }

            return incidents;
        }

        if (!anyStall && sample.FrameMs > t.UnattributedMs)
        {
            incidents.Add(new Incident(sample.Timestamp, null, IncidentRule.UnattributedStall, sample.FrameMs, t.UnattributedMs, "Frame"));
        }

        return incidents;
    }

    public Incident? Process(ErrorEvent error)
    {
        if (this.Paused || this.Suppressed || error.InternalName is null || this.IsIgnored(error.InternalName))
        {
            return null;
        }

        var t = this.Thresholds;
        var window = this.Window(error.InternalName);
        var count = window.AddError(error.Timestamp, t.ErrorWindowSeconds);
        if (count < t.ErrorCount)
        {
            return null;
        }

        if (window.LastErrorsIncidentAt is { } last && (error.Timestamp - last).TotalSeconds < t.ErrorWindowSeconds)
        {
            return null;
        }

        window.LastErrorsIncidentAt = error.Timestamp;
        return new Incident(error.Timestamp, error.InternalName, IncidentRule.Errors, count, t.ErrorCount, error.Message);
    }

    public Incident? Process(HitchEvent hitch)
    {
        if (this.Paused || this.Suppressed || (hitch.InternalName is { } name && this.IsIgnored(name)))
        {
            return null;
        }

        return hitch.InternalName is null
            ? new Incident(hitch.Timestamp, null, IncidentRule.UnattributedStall, hitch.Ms, hitch.LimitMs, hitch.Source)
            : new Incident(hitch.Timestamp, hitch.InternalName, IncidentRule.Stall, hitch.Ms, hitch.LimitMs, hitch.Source);
    }

    public PluginWindow? Snapshot(string internalName) =>
        this.windows.GetValueOrDefault(internalName);

    public void Reset() => this.windows.Clear();

    private PluginWindow Window(string internalName)
    {
        if (!this.windows.TryGetValue(internalName, out var window))
        {
            window = new PluginWindow();
            this.windows[internalName] = window;
        }

        return window;
    }
}
