using Culprit.Core;
using Culprit.Core.Detection;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class EngineGcTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GcHeavyFrame_ProducesOneUnattributedStallWithGcDetailAndUpdatesSnapshot()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(new FrameSample(T0, 120, [new PluginTiming("P", 90, 0)], 110));

        var i = Assert.Single(incidents);
        Assert.Equal(IncidentRule.UnattributedStall, i.Rule);
        Assert.Equal("GC pause 110 ms", i.Detail);
        Assert.Equal(90, engine.Snapshot("P")!.LongestDrawMs);
    }

    [Fact]
    public void SmallGcPause_StillReportsStallForThePlugin()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(new FrameSample(T0, 120, [new PluginTiming("P", 90, 0)], 20));

        var i = Assert.Single(incidents);
        Assert.Equal(IncidentRule.Stall, i.Rule);
        Assert.Equal("P", i.InternalName);
    }

    [Fact]
    public void GcHeavyFrameUnderUnattributedLimit_ProducesNothing()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(new FrameSample(T0, 60, [new PluginTiming("P", 5, 0)], 55));

        Assert.Empty(incidents);
    }

    [Fact]
    public void GcHeavyFrames_DoNotFeedTheSustainedWindow()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = new List<Incident>();
        var frames = (int)(5.1 * 60);
        for (var i = 0; i <= frames; i++)
        {
            var ts = T0.AddMilliseconds(i * 16.667);
            incidents.AddRange(engine.Process(new FrameSample(ts, 120, [new PluginTiming("P", 4, 0)], 100)));
        }

        Assert.DoesNotContain(incidents, inc => inc.Rule == IncidentRule.Sustained);
    }
}
