using Culprit.Core.Detection;
using Xunit;

namespace Culprit.Tests;

public class GracePeriodTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverMarked_IsInactive()
    {
        var grace = new GracePeriod(TimeSpan.FromSeconds(5));

        Assert.False(grace.IsActive(T0));
    }

    [Fact]
    public void ActiveUntilLengthElapses()
    {
        var grace = new GracePeriod(TimeSpan.FromSeconds(5));
        grace.MarkChange(T0);

        Assert.True(grace.IsActive(T0));
        Assert.True(grace.IsActive(T0.AddSeconds(4.9)));
        Assert.False(grace.IsActive(T0.AddSeconds(5)));
    }

    [Fact]
    public void MarkingAgain_Extends()
    {
        var grace = new GracePeriod(TimeSpan.FromSeconds(5));
        grace.MarkChange(T0);
        grace.MarkChange(T0.AddSeconds(4));

        Assert.True(grace.IsActive(T0.AddSeconds(8)));
    }
}
