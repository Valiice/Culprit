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
