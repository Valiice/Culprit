using Culprit.Core.Detection;
using Xunit;

namespace Culprit.Tests;

public class SuppressionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    [Fact]
    public void NotBlockedAndNeverMarked_IsFalse()
    {
        var tracker = new SuppressionTracker(Grace);

        Assert.False(tracker.Evaluate(blockedNow: false, T0));
    }

    [Fact]
    public void Blocked_IsTrue()
    {
        var tracker = new SuppressionTracker(Grace);

        Assert.True(tracker.Evaluate(blockedNow: true, T0));
    }

    [Fact]
    public void AfterBlockedTick_UnblockedStaysTrueUntilGraceElapses()
    {
        var tracker = new SuppressionTracker(Grace);
        tracker.Evaluate(blockedNow: true, T0);
        tracker.Evaluate(blockedNow: false, T0);

        Assert.True(tracker.Evaluate(blockedNow: false, T0.AddSeconds(1)));
        Assert.False(tracker.Evaluate(blockedNow: false, T0.AddSeconds(5)));
    }

    [Fact]
    public void MarkPluginChange_SuppressesUnderGraceButNotAt()
    {
        var tracker = new SuppressionTracker(Grace);
        tracker.MarkPluginChange(T0);

        Assert.True(tracker.Evaluate(blockedNow: false, T0.AddSeconds(4.9)));
        Assert.False(tracker.Evaluate(blockedNow: false, T0.AddSeconds(5)));
    }

    [Fact]
    public void BlockClearingDuringPluginGrace_StaysSuppressedUntilLaterExpiry()
    {
        var tracker = new SuppressionTracker(Grace);
        tracker.MarkPluginChange(T0);
        tracker.Evaluate(blockedNow: true, T0.AddSeconds(3));

        // Block clears at T0+4: plugin grace expires at T0+5, block grace at T0+4+5=T0+9.
        Assert.True(tracker.Evaluate(blockedNow: false, T0.AddSeconds(4)));
        Assert.True(tracker.Evaluate(blockedNow: false, T0.AddSeconds(8.9)));
        Assert.False(tracker.Evaluate(blockedNow: false, T0.AddSeconds(9)));
    }
}
