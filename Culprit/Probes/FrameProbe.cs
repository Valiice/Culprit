using System.Diagnostics;

namespace Culprit.Probes;

/// <summary>Whole-frame time measured between consecutive framework ticks. No reflection; always available.</summary>
internal sealed class FrameProbe
{
    private long lastTimestamp;

    public ProbeStatus Status { get; } = ProbeStatus.Ok("Frame time");

    /// <summary>Milliseconds since the previous tick, or null on the first call.</summary>
    public double? Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var previous = this.lastTimestamp;
        this.lastTimestamp = now;
        return previous == 0 ? null : Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;
    }
}
