namespace Culprit.Core.Detection;

/// <summary>
/// Rolling per-plugin state: latest and longest timings, the samples of the current
/// sustained window, and recent error timestamps. Not thread safe; the engine owns it.
/// </summary>
public sealed class PluginWindow
{
    private readonly List<double> samples = [];
    private readonly Queue<DateTime> errors = new();
    private readonly Dictionary<string, DateTime> lastStallAt = new(StringComparer.Ordinal);
    private DateTime? windowStart;
    private double lastCompletedAverage;

    public double LastDrawMs { get; private set; }

    public double LastUpdateMs { get; private set; }

    public double LongestDrawMs { get; private set; }

    public double LongestUpdateMs { get; private set; }

    /// <summary>When the last Errors incident was emitted for this plugin, so the engine can rate-limit them.</summary>
    public DateTime? LastErrorsIncidentAt { get; set; }

    /// <summary>
    /// Seconds the engine currently waits between Errors incidents for this plugin. Zero means
    /// no incident has fired yet, so the engine falls back to Thresholds.ErrorWindowSeconds.
    /// Doubles after each consecutive incident and is capped at 60 minutes; see Engine.
    /// </summary>
    public double ErrorsIncidentInterval { get; set; }

    /// <summary>When the error count for this plugin last reached the Errors threshold, incident emitted or not; used to reset the backoff after an hour of quiet.</summary>
    public DateTime? LastErrorsThresholdAt { get; set; }

    /// <summary>Mean of the current window, or of the last completed one while the current is empty.</summary>
    public double CurrentAverageMs => this.samples.Count == 0 ? this.lastCompletedAverage : this.samples.Average();

    /// <summary>
    /// Records a frame's timings. Last and Longest always update. The sample is folded into the
    /// sustained window (and the window started if it was not already) only when includeInAverage
    /// is true, so a frame that already got reported as a stall does not also echo into Sustained.
    /// </summary>
    public void AddTiming(DateTime now, double drawMs, double updateMs, bool includeInAverage)
    {
        this.LastDrawMs = drawMs;
        this.LastUpdateMs = updateMs;
        this.LongestDrawMs = Math.Max(this.LongestDrawMs, drawMs);
        this.LongestUpdateMs = Math.Max(this.LongestUpdateMs, updateMs);

        if (includeInAverage)
        {
            this.windowStart ??= now;
            this.samples.Add(drawMs + updateMs);
        }
    }

    /// <summary>
    /// Returns the window mean and starts a new window once windowSeconds have elapsed since the
    /// window began; otherwise null. An empty due window restarts without reporting. A window that
    /// spans a gap more than twice as long as windowSeconds (suppression or pause) is abandoned
    /// instead of completed: its stale pre-gap samples are discarded and lastCompletedAverage is
    /// left untouched, so a resumed window is judged only on fresh samples.
    /// </summary>
    public double? CompleteWindowIfDue(DateTime now, double windowSeconds)
    {
        if (this.windowStart is not { } start)
        {
            return null;
        }

        var elapsed = (now - start).TotalSeconds;
        if (elapsed < windowSeconds)
        {
            return null;
        }

        this.windowStart = now;
        if (elapsed > 2 * windowSeconds)
        {
            this.samples.Clear();
            return null;
        }

        if (this.samples.Count == 0)
        {
            return null;
        }

        var average = this.samples.Average();
        this.lastCompletedAverage = average;
        this.samples.Clear();
        return average;
    }

    /// <summary>Records an error and returns how many fall inside the trailing window.</summary>
    public int AddError(DateTime now, double windowSeconds)
    {
        this.errors.Enqueue(now);
        while (this.errors.Count > 0 && (now - this.errors.Peek()).TotalSeconds > windowSeconds)
        {
            this.errors.Dequeue();
        }

        return this.errors.Count;
    }

    public void ClearErrors() => this.errors.Clear();

    /// <summary>
    /// Coalesces repeated stalls from the same source (Draw or Update) so a chronic offender
    /// cannot flood the incident log: returns true and records now at most once per second per
    /// source, false otherwise.
    /// </summary>
    public bool ShouldEmitStall(string source, DateTime now)
    {
        if (this.lastStallAt.TryGetValue(source, out var last) && (now - last).TotalSeconds < 1)
        {
            return false;
        }

        this.lastStallAt[source] = now;
        return true;
    }
}
