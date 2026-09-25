using System.Diagnostics;

namespace Culprit.Probes;

/// <summary>One tick's frame time (null on the first call) and the GC pause since the previous tick.</summary>
internal sealed record FrameReading(double? FrameMs, double GcPauseMs);

/// <summary>
/// Whole-frame time measured between consecutive framework ticks, plus the .NET GC pause time
/// that occurred since the previous tick. No reflection; always available.
/// </summary>
internal sealed class FrameProbe
{
    private long lastTimestamp;
    private double lastTotalPauseMs;

    public ProbeStatus Status { get; } = ProbeStatus.Ok("Frame time");

    /// <summary>Total milliseconds of GC pause since the process started.</summary>
    public double TotalPauseMs => GC.GetTotalPauseDuration().TotalMilliseconds;

    /// <summary>Number of generation-2 collections since the process started.</summary>
    public int Gen2Collections => GC.CollectionCount(2);

    /// <summary>Milliseconds since the previous tick and the GC pause since then; both are null/0 on the first call.</summary>
    public FrameReading Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var previous = this.lastTimestamp;
        this.lastTimestamp = now;

        var totalPause = this.TotalPauseMs;
        var gcPauseMs = totalPause - this.lastTotalPauseMs;
        this.lastTotalPauseMs = totalPause;

        return previous == 0
            ? new FrameReading(null, 0)
            : new FrameReading(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds, gcPauseMs);
    }
}
