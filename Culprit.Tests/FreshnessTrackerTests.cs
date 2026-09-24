using Culprit.Core.Sampling;
using Xunit;

namespace Culprit.Tests;

public class FreshnessTrackerTests
{
    [Fact]
    public void UnseenKey_IsNew()
    {
        var tracker = new FreshnessTracker();

        Assert.True(tracker.IsNew("A.Plugin::OnUpdate", 1));
    }

    [Fact]
    public void UnchangedCount_IsNotNew()
    {
        var tracker = new FreshnessTracker();
        tracker.Record("k", 10);

        Assert.False(tracker.IsNew("k", 10));
    }

    [Fact]
    public void GrownCount_IsNew()
    {
        var tracker = new FreshnessTracker();
        tracker.Record("k", 10);

        Assert.True(tracker.IsNew("k", 11));
    }

    [Fact]
    public void AfterTrimRecord_NextGrowthIsStillDetected()
    {
        var tracker = new FreshnessTracker();
        tracker.Record("k", 501);
        tracker.Record("k", FreshnessTracker.TrimTo);

        Assert.False(tracker.IsNew("k", FreshnessTracker.TrimTo));
        Assert.True(tracker.IsNew("k", FreshnessTracker.TrimTo + 1));
    }

    [Fact]
    public void ShouldTrim_OnlyAboveLimit()
    {
        Assert.False(FreshnessTracker.ShouldTrim(500));
        Assert.True(FreshnessTracker.ShouldTrim(501));
    }

    [Fact]
    public void Forget_MakesKeyNewAgain()
    {
        var tracker = new FreshnessTracker();
        tracker.Record("k", 10);
        tracker.Forget("k");

        Assert.True(tracker.IsNew("k", 10));
    }
}
