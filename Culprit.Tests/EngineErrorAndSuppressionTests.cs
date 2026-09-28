using Culprit.Core;
using Culprit.Core.Detection;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class EngineErrorAndSuppressionTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static ErrorEvent Err(string? plugin, int secondsAfter = 0) =>
        new(T0.AddSeconds(secondsAfter), plugin, "Exception in event handler OnUpdate");

    [Fact]
    public void FifthErrorInWindow_ProducesErrorsIncident()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        for (var i = 0; i < 4; i++)
        {
            Assert.Null(engine.Process(Err("Thrower", i)));
        }

        var incident = engine.Process(Err("Thrower", 4));

        Assert.NotNull(incident);
        Assert.Equal(IncidentRule.Errors, incident.Rule);
        Assert.Equal("Thrower", incident.InternalName);
        Assert.Equal(5, incident.Value);
        Assert.Equal(5, incident.Limit);
        Assert.Equal("Exception in event handler OnUpdate", incident.Detail);
    }

    [Fact]
    public void ErrorsOutsideWindow_DoNotCount()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        for (var i = 0; i < 4; i++)
        {
            engine.Process(Err("Slow", i));
        }

        var incident = engine.Process(Err("Slow", 61));

        Assert.Null(incident);
    }

    [Fact]
    public void AfterAnErrorsIncident_NoSecondIncidentWithinTheWindow()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        for (var i = 0; i < 5; i++)
        {
            engine.Process(Err("Thrower", i));
        }

        Assert.Null(engine.Process(Err("Thrower", 5)));
    }

    [Fact]
    public void AfterTheErrorWindow_ErrorsIncidentFiresAgain()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(i == 4, engine.Process(Err("Thrower", i)) is not null);
        }

        var fired = 0;
        for (var i = 61; i <= 65; i++)
        {
            if (engine.Process(Err("Thrower", i)) is not null)
            {
                fired++;
            }
        }

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ChronicOffender_ErrorsIncidentIntervalDoublesEachTime()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        var fired = new List<Incident>();

        for (var i = 0; fired.Count < 9; i++)
        {
            var incident = engine.Process(Err("Thrower", i));
            if (incident is not null)
            {
                fired.Add(incident);
            }
        }

        Assert.Equal(T0.AddSeconds(4), fired[0].Timestamp);
        Assert.Equal(T0.AddSeconds(64), fired[1].Timestamp);
        Assert.Equal(T0.AddSeconds(184), fired[2].Timestamp);
        Assert.Equal(T0.AddSeconds(424), fired[3].Timestamp);
        Assert.Equal(T0.AddSeconds(904), fired[4].Timestamp);
        Assert.Equal(T0.AddSeconds(1864), fired[5].Timestamp);
        Assert.Equal(T0.AddSeconds(3784), fired[6].Timestamp);
        Assert.Equal(T0.AddSeconds(7384), fired[7].Timestamp);
        Assert.Equal(T0.AddSeconds(10984), fired[8].Timestamp);

        // Once the interval hits the 60-minute cap, incidents keep arriving exactly an hour apart.
        Assert.Equal(3600, (fired[8].Timestamp - fired[7].Timestamp).TotalSeconds);
    }

    [Fact]
    public void ChronicOffender_ResetsBackoffAfterAQuietHour()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        for (var i = 0; i <= 64; i++)
        {
            engine.Process(Err("Thrower", i));
        }

        // Second incident fired at t=64 with the interval now doubled to 120 s for the next one.
        Assert.Equal(64, (engine.Snapshot("Thrower")!.LastErrorsIncidentAt!.Value - T0).TotalSeconds);

        // Quiet for over an hour: no more errors at all, so the threshold is never reached again
        // until the plugin resumes.
        var resumeAt = 64 + 3601;

        Incident? first = null;
        for (var i = 0; i <= 4; i++)
        {
            first = engine.Process(Err("Thrower", resumeAt + i));
        }

        Assert.NotNull(first);
        Assert.Equal(T0.AddSeconds(resumeAt + 4), first!.Timestamp);

        // The backoff reset, so the next incident needs only the base 60 s interval again, not 120 s.
        Incident? second = null;
        for (var i = 5; i <= 64; i++)
        {
            var incident = engine.Process(Err("Thrower", resumeAt + i));
            if (incident is not null)
            {
                second = incident;
            }
        }

        Assert.NotNull(second);
        Assert.Equal(T0.AddSeconds(resumeAt + 64), second!.Timestamp);
    }

    [Fact]
    public void UnattributedError_IsDropped()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(engine.Process(Err(null, i)));
        }
    }

    [Fact]
    public void Hitch_BecomesStall()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incident = engine.Process(new HitchEvent(T0, "Heavy", 123.4, 100, "Draw"));

        Assert.NotNull(incident);
        Assert.Equal(IncidentRule.Stall, incident.Rule);
        Assert.Equal("Heavy", incident.InternalName);
        Assert.Equal(123.4, incident.Value);
        Assert.Equal(100, incident.Limit);
        Assert.Equal("Draw", incident.Detail);
    }

    [Fact]
    public void UnattributedHitch_BecomesUnattributedStall()
    {
        var engine = new Engine(new Thresholds(), "Culprit");

        var incident = engine.Process(new HitchEvent(T0, null, 80, 50, "Frame"));

        Assert.NotNull(incident);
        Assert.Equal(IncidentRule.UnattributedStall, incident.Rule);
        Assert.Null(incident.InternalName);
    }

    [Fact]
    public void Suppressed_ProducesNothingAndDoesNotFeedWindows()
    {
        var engine = new Engine(new Thresholds(), "Culprit") { Suppressed = true };

        Assert.Empty(engine.Process(new FrameSample(T0, 500, [new PluginTiming("P", 400, 0)], 0)));
        Assert.Null(engine.Process(Err("P")));
        Assert.Null(engine.Process(new HitchEvent(T0, "P", 500, 100, "Draw")));
        Assert.Null(engine.Snapshot("P"));
    }

    [Fact]
    public void Paused_ProducesNothing()
    {
        var engine = new Engine(new Thresholds(), "Culprit") { Paused = true };

        Assert.Empty(engine.Process(new FrameSample(T0, 500, [new PluginTiming("P", 400, 0)], 0)));
        Assert.Null(engine.Process(Err("P")));
        Assert.Null(engine.Process(new HitchEvent(T0, "P", 500, 100, "Draw")));

        var snapshot = engine.Snapshot("P");
        Assert.NotNull(snapshot);
        Assert.Equal(400, snapshot.LastDrawMs);
    }

    [Fact]
    public void IgnoredPlugin_ProducesNothing()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Ignored.Add("noisy");

        Assert.Empty(engine.Process(new FrameSample(T0, 10, [new PluginTiming("Noisy", 400, 0)], 0)));
        for (var i = 0; i < 6; i++)
        {
            Assert.Null(engine.Process(Err("Noisy", i)));
        }

        Assert.Null(engine.Process(new HitchEvent(T0, "Noisy", 500, 100, "Draw")));
    }

    [Fact]
    public void Reset_ForgetsWindows()
    {
        var engine = new Engine(new Thresholds(), "Culprit");
        engine.Process(new FrameSample(T0, 10, [new PluginTiming("P", 5, 5)], 0));
        for (var i = 0; i < 4; i++)
        {
            engine.Process(Err("P", i));
        }

        engine.Reset();

        Assert.Null(engine.Snapshot("P"));
        Assert.Null(engine.Process(Err("P", 4)));
    }
}
