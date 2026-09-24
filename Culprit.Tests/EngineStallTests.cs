using Culprit.Core;
using Culprit.Core.Detection;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class EngineStallTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static FrameSample Frame(double frameMs, params PluginTiming[] plugins) =>
        new(T0, frameMs, plugins);

    [Fact]
    public void DrawAboveStallMs_ProducesStallIncident()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(30, new PluginTiming("Heavy", 25, 0)));

        var i = Assert.Single(incidents);
        Assert.Equal("Heavy", i.InternalName);
        Assert.Equal(IncidentRule.Stall, i.Rule);
        Assert.Equal(25, i.Value);
        Assert.Equal(20, i.Limit);
        Assert.Equal("Draw", i.Detail);
    }

    [Fact]
    public void UpdateAboveStallMs_ProducesStallIncidentWithUpdateDetail()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(30, new PluginTiming("Heavy", 0, 21)));

        var i = Assert.Single(incidents);
        Assert.Equal(IncidentRule.Stall, i.Rule);
        Assert.Equal("Update", i.Detail);
    }

    [Fact]
    public void ExactlyStallMs_IsNotAStall()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(10, new PluginTiming("Fine", 20, 20)));

        Assert.Empty(incidents);
    }

    [Fact]
    public void DrawAndUpdateBothOver_ProduceTwoIncidents()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(60, new PluginTiming("Heavy", 25, 25)));

        Assert.Equal(2, incidents.Count);
    }

    [Fact]
    public void LongFrameWithNoPluginStall_IsUnattributed()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(150, new PluginTiming("Fine", 1, 1)));

        var i = Assert.Single(incidents);
        Assert.Null(i.InternalName);
        Assert.Equal(IncidentRule.UnattributedStall, i.Rule);
        Assert.Equal(150, i.Value);
        Assert.Equal(100, i.Limit);
    }

    [Fact]
    public void LongFrameWithAPluginStall_IsNotAlsoUnattributed()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(150, new PluginTiming("Heavy", 120, 0)));

        var i = Assert.Single(incidents);
        Assert.Equal(IncidentRule.Stall, i.Rule);
    }

    [Fact]
    public void FrameExactlyUnattributedMs_IsNotUnattributed()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        Assert.Empty(engine.Process(Frame(100)));
    }

    [Fact]
    public void SelfIsAlwaysIgnored()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = engine.Process(Frame(10, new PluginTiming("Culprit", 500, 500)));

        Assert.Empty(incidents);
        Assert.True(engine.IsIgnored("Culprit"));
    }

    [Fact]
    public void Snapshot_TracksLastAndLongest()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Process(Frame(10, new PluginTiming("P", 5, 2)));
        engine.Process(Frame(10, new PluginTiming("P", 3, 4)));

        var w = engine.Snapshot("P")!;

        Assert.Equal(3, w.LastDrawMs);
        Assert.Equal(4, w.LastUpdateMs);
        Assert.Equal(5, w.LongestDrawMs);
        Assert.Equal(4, w.LongestUpdateMs);
        Assert.Contains("P", engine.KnownPlugins);
    }

    [Fact]
    public void ThresholdsCanChangeAtRuntime()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Thresholds = new Thresholds(StallMs: 50);

        Assert.Empty(engine.Process(Frame(10, new PluginTiming("P", 30, 0))));
    }

    [Fact]
    public void RepeatedStall_IsCoalescedWithinOneSecond()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = new List<Incident>();
        for (var i = 0; i < 3; i++)
        {
            var ts = T0.AddMilliseconds(i * 16);
            incidents.AddRange(engine.Process(new FrameSample(ts, 30, [new PluginTiming("Heavy", 30, 0)])));
        }

        Assert.Single(incidents, i => i.Rule == IncidentRule.Stall);
    }

    [Fact]
    public void StallAfterOneSecond_FiresAgain()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = new List<Incident>();
        for (var i = 0; i < 3; i++)
        {
            var ts = T0.AddMilliseconds(i * 16);
            incidents.AddRange(engine.Process(new FrameSample(ts, 30, [new PluginTiming("Heavy", 30, 0)])));
        }

        incidents.AddRange(engine.Process(new FrameSample(T0.AddSeconds(1.1), 30, [new PluginTiming("Heavy", 30, 0)])));

        Assert.Equal(2, incidents.Count(i => i.Rule == IncidentRule.Stall));
    }
}
