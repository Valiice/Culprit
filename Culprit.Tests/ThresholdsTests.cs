using Culprit.Core;
using Xunit;

namespace Culprit.Tests;

public class ThresholdsTests
{
    [Fact]
    public void Defaults_MatchTheSpec()
    {
        var t = new Thresholds();

        Assert.Equal(40, t.StallMs);
        Assert.Equal(3, t.SustainedMs);
        Assert.Equal(5, t.SustainedWindowSeconds);
        Assert.Equal(5, t.ErrorCount);
        Assert.Equal(60, t.ErrorWindowSeconds);
        Assert.Equal(100, t.UnattributedMs);
    }
}
