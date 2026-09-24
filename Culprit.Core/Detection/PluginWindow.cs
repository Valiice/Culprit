namespace Culprit.Core.Detection;

/// <summary>
/// Rolling per-plugin state: latest and longest timings, the samples of the current
/// sustained window, and recent error timestamps. Not thread safe; the engine owns it.
/// </summary>
public sealed class PluginWindow
{
    private readonly List<double> samples = [];
    private readonly Queue<DateTime> errors = new();
    private DateTime? windowStart;
    private double lastCompletedAverage;

    public double LastDrawMs { get; private set; }

    public double LastUpdateMs { get; private set; }

    public double LongestDrawMs { get; private set; }

    public double LongestUpdateMs { get; private set; }

    /// <summary>Mean of the current window, or of the last completed one while the current is empty.</summary>
    public double CurrentAverageMs => this.samples.Count == 0 ? this.lastCompletedAverage : this.samples.Average();

    public void AddTiming(DateTime now, double drawMs, double updateMs)
    {
        this.LastDrawMs = drawMs;
        this.LastUpdateMs = updateMs;
        this.LongestDrawMs = Math.Max(this.LongestDrawMs, drawMs);
        this.LongestUpdateMs = Math.Max(this.LongestUpdateMs, updateMs);
        this.windowStart ??= now;
        this.samples.Add(drawMs + updateMs);
    }

    /// <summary>
    /// Returns the window mean and starts a new window once windowSeconds have elapsed since the
    /// window began; otherwise null. An empty due window restarts without reporting.
    /// </summary>
    public double? CompleteWindowIfDue(DateTime now, double windowSeconds)
    {
        if (this.windowStart is not { } start || (now - start).TotalSeconds < windowSeconds)
        {
            return null;
        }

        this.windowStart = now;
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
}
