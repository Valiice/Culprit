using Culprit.Core;
using Culprit.Core.Detection;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class EngineSustainedTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Feeds one sample per 16 ms frame for the given number of seconds, returning all incidents.</summary>
    private static List<Incident> Run(Engine engine, string plugin, double drawMs, double updateMs, double seconds, DateTime start)
    {
        var all = new List<Incident>();
        var frames = (int)(seconds * 60);
        for (var i = 0; i <= frames; i++)
        {
            var ts = start.AddMilliseconds(i * 16.667);
            all.AddRange(engine.Process(new FrameSample(ts, 16, [new PluginTiming(plugin, drawMs, updateMs)], 0)));
        }

        return all;
    }

    [Fact]
    public void SteadyDrainAboveLimit_ProducesOneSustainedPerWindow()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = Run(engine, "Drain", 2, 1.1, 5.1, T0);

        var sustained = incidents.Where(i => i.Rule == IncidentRule.Sustained).ToList();
        var s = Assert.Single(sustained);
        Assert.Equal("Drain", s.InternalName);
        Assert.Equal(3.1, s.Value, 3);
        Assert.Equal(3, s.Limit);
        Assert.Equal("5 s window", s.Detail);
    }

    [Fact]
    public void SteadyDrainBelowLimit_ProducesNothing()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = Run(engine, "Ok", 1, 1, 11, T0);

        Assert.Empty(incidents);
    }

    [Fact]
    public void ExactlyAtLimit_ProducesNothing()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = Run(engine, "Edge", 3, 0, 11, T0);

        Assert.Empty(incidents);
    }

    [Fact]
    public void SingleSpike_DoesNotTriggerSustained()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        // 19 ms once (under the 20 ms stall limit), then quiet for the rest of the window.
        engine.Process(new FrameSample(T0, 16, [new PluginTiming("Spiky", 19, 0)], 0));

        var incidents = Run(engine, "Spiky", 0.1, 0, 5.1, T0.AddMilliseconds(17));

        Assert.DoesNotContain(incidents, i => i.Rule == IncidentRule.Sustained);
    }

    [Fact]
    public void TwoWindows_ProduceTwoSustainedIncidents()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = Run(engine, "Drain", 4, 0, 10.2, T0);

        Assert.Equal(2, incidents.Count(i => i.Rule == IncidentRule.Sustained));
    }

    [Fact]
    public void CurrentAverage_ReflectsTheOpenWindow()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Process(new FrameSample(T0, 16, [new PluginTiming("P", 2, 2)], 0));
        engine.Process(new FrameSample(T0.AddMilliseconds(16), 16, [new PluginTiming("P", 4, 4)], 0));

        Assert.Equal(6, engine.Snapshot("P")!.CurrentAverageMs, 6);
    }

    [Fact]
    public void WindowLengthFollowsThresholds()
    {
        var engine = new Engine(new Thresholds(SustainedWindowSeconds: 2), "Culprit");

        var incidents = Run(engine, "Drain", 4, 0, 2.1, T0);

        Assert.Single(incidents, i => i.Rule == IncidentRule.Sustained);
    }

    [Fact]
    public void WindowSpanningAGap_IsAbandonedNotReported()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var before = Run(engine, "Gappy", 4, 0, 1, T0);
        // No samples fed for 12 seconds: suppression or pause without calling Process.
        var after = Run(engine, "Gappy", 4, 0, 0, T0.AddSeconds(13));

        var all = before.Concat(after);
        Assert.DoesNotContain(all, i => i.Rule == IncidentRule.Sustained);
    }

    [Fact]
    public void WindowResumesAfterGap()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var before = Run(engine, "Gappy", 4, 0, 1, T0);
        var after = Run(engine, "Gappy", 4, 0, 5.1, T0.AddSeconds(13));

        var all = before.Concat(after);
        Assert.Single(all, i => i.Rule == IncidentRule.Sustained);
    }

    [Fact]
    public void HugeStallFrame_DoesNotEchoAsSustained()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        var initial = engine.Process(new FrameSample(T0, 16, [new PluginTiming("Heavy", 600, 0)], 0));

        var incidents = initial.Concat(Run(engine, "Heavy", 0.1, 0, 5.2, T0.AddMilliseconds(17))).ToList();

        Assert.DoesNotContain(incidents, i => i.Rule == IncidentRule.Sustained);
        Assert.Single(incidents, i => i.Rule == IncidentRule.Stall);
    }

    [Fact]
    public void StallFrame_StillUpdatesLastAndLongest()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Process(new FrameSample(T0, 16, [new PluginTiming("Heavy", 600, 0)], 0));

        Assert.Equal(600, engine.Snapshot("Heavy")!.LongestDrawMs);
        Assert.Equal(600, engine.Snapshot("Heavy")!.LastDrawMs);
    }

    [Fact]
    public void SteadyDrainBelowStall_StillCountsTowardSustained()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incidents = Run(engine, "Drain", 15, 0, 5.1, T0);

        Assert.Single(incidents, i => i.Rule == IncidentRule.Sustained);
    }
}
