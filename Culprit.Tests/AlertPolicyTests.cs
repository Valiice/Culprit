using Culprit.Core.Alerting;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class AlertPolicyTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly AlertSettings Defaults = new(TimeSpan.FromMinutes(5), Muted: false, NotifyUnattributed: false, WarnOnFirstIncident: true, RepeatWindow: TimeSpan.FromMinutes(10));

    private static Incident Stall(string? plugin, int secondsAfter = 0) =>
        new(T0.AddSeconds(secondsAfter), plugin, IncidentRule.Stall, 48, 20, "Draw");

    private static Incident Sustained(string? plugin, int secondsAfter = 0) =>
        new(T0.AddSeconds(secondsAfter), plugin, IncidentRule.Sustained, 4, 3, "5 s window");

    private static Incident Errors(string plugin, int secondsAfter = 0) =>
        new(T0.AddSeconds(secondsAfter), plugin, IncidentRule.Errors, 5, 5, "boom");

    private static Incident Unattributed(int secondsAfter = 0) =>
        new(T0.AddSeconds(secondsAfter), null, IncidentRule.UnattributedStall, 150, 100, "Frame");

    [Fact]
    public void FirstIncident_Notifies()
    {
        var policy = new AlertPolicy(Defaults);

        var d = policy.Decide(Stall("A"), T0);

        Assert.True(d.Notify);
        Assert.Equal(0, d.SuppressedSinceLast);
    }

    [Fact]
    public void WithinCooldown_LogsOnly()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);

        var d = policy.Decide(Stall("A", 60), T0.AddSeconds(60));

        Assert.False(d.Notify);
    }

    [Fact]
    public void AfterCooldown_NotifiesWithSuppressedCount()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);
        policy.Decide(Stall("A", 10), T0.AddSeconds(10));
        policy.Decide(Stall("A", 20), T0.AddSeconds(20));
        policy.Decide(Stall("A", 30), T0.AddSeconds(30));

        var d = policy.Decide(Stall("A", 301), T0.AddSeconds(301));

        Assert.True(d.Notify);
        Assert.Equal(3, d.SuppressedSinceLast);

        // This is the third toast for the key, so the backoff from the second toast (5 min ->
        // 10 min) now applies: it needs the full doubled cooldown, not just the original 5 min.
        var next = policy.Decide(Stall("A", 901), T0.AddSeconds(901));
        Assert.Equal(0, next.SuppressedSinceLast);
    }

    [Fact]
    public void ExactlyAtCooldown_Notifies()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);

        Assert.True(policy.Decide(Stall("A", 300), T0.AddSeconds(300)).Notify);
    }

    [Fact]
    public void DifferentRulesForSamePlugin_AreIndependent()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);

        Assert.True(policy.Decide(Errors("A", 1), T0.AddSeconds(1)).Notify);
    }

    [Fact]
    public void DifferentPlugins_AreIndependent()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);

        Assert.True(policy.Decide(Stall("B", 1), T0.AddSeconds(1)).Notify);
    }

    [Fact]
    public void Muted_NeverNotifiesButKeepsCounting()
    {
        var policy = new AlertPolicy(Defaults with { Muted = true });

        Assert.False(policy.Decide(Stall("A"), T0).Notify);
        Assert.False(policy.Decide(Stall("A", 1), T0.AddSeconds(1)).Notify);

        policy.Settings = Defaults;
        var d = policy.Decide(Stall("A", 2), T0.AddSeconds(2));

        Assert.True(d.Notify);
        Assert.Equal(2, d.SuppressedSinceLast);
    }

    [Fact]
    public void Unattributed_LogsOnlyUnlessEnabled()
    {
        var policy = new AlertPolicy(Defaults);
        Assert.False(policy.Decide(Unattributed(), T0).Notify);

        policy.Settings = Defaults with { NotifyUnattributed = true };
        Assert.True(policy.Decide(Unattributed(1), T0.AddSeconds(1)).Notify);
    }

    [Fact]
    public void CooldownFollowsSettings()
    {
        var policy = new AlertPolicy(Defaults with { Cooldown = TimeSpan.FromSeconds(10) });
        policy.Decide(Stall("A"), T0);

        Assert.False(policy.Decide(Stall("A", 9), T0.AddSeconds(9)).Notify);
        Assert.True(policy.Decide(Stall("A", 10), T0.AddSeconds(10)).Notify);
    }

    [Fact]
    public void Reset_ClearsCooldowns()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);
        policy.Reset();

        Assert.True(policy.Decide(Stall("A", 1), T0.AddSeconds(1)).Notify);
    }

    [Fact]
    public void SecondStrike_FirstStallIsNotNotified()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = false, RepeatWindow = TimeSpan.FromMinutes(10) });

        var d = policy.Decide(Stall("A"), T0);

        Assert.False(d.Notify);
    }

    [Fact]
    public void SecondStrike_SecondStallWithinWindowNotifiesWithSuppressedCount()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = false, RepeatWindow = TimeSpan.FromMinutes(10) });
        policy.Decide(Stall("A"), T0);

        var d = policy.Decide(Stall("A", 30), T0.AddSeconds(30));

        Assert.True(d.Notify);
        Assert.Equal(1, d.SuppressedSinceLast);
    }

    [Fact]
    public void SecondStrike_SecondStallAfterWindowExpiresCountsAsNewFirst()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = false, RepeatWindow = TimeSpan.FromMinutes(10) });
        policy.Decide(Stall("A"), T0);

        var d = policy.Decide(Stall("A", 660), T0.AddMinutes(11));

        Assert.False(d.Notify);
    }

    [Fact]
    public void SecondStrike_ErrorsNotifyOnFirstOccurrenceEvenWithFlagFalse()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = false, RepeatWindow = TimeSpan.FromMinutes(10) });

        var d = policy.Decide(Errors("A"), T0);

        Assert.True(d.Notify);
    }

    [Fact]
    public void SecondStrike_WhenWarnOnFirstIncidentTrue_FirstStallNotifies()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = true, RepeatWindow = TimeSpan.FromMinutes(10) });

        var d = policy.Decide(Stall("A"), T0);

        Assert.True(d.Notify);
    }

    [Fact]
    public void SecondStrike_SustainedObeysTheSameRuleAsStall()
    {
        var policy = new AlertPolicy(Defaults with { WarnOnFirstIncident = false, RepeatWindow = TimeSpan.FromMinutes(10) });

        Assert.False(policy.Decide(Sustained("A"), T0).Notify);
        Assert.True(policy.Decide(Sustained("A", 30), T0.AddSeconds(30)).Notify);
    }

    [Fact]
    public void Chronic_CooldownDoublesWhileIncidentsKeepArriving()
    {
        var policy = new AlertPolicy(Defaults);
        var fired = new List<DateTime>();

        for (var i = 0; fired.Count < 4; i += 10)
        {
            var d = policy.Decide(Stall("A", i), T0.AddSeconds(i));
            if (d.Notify)
            {
                fired.Add(T0.AddSeconds(i));
            }
        }

        Assert.Equal(T0, fired[0]);
        Assert.Equal(T0.AddMinutes(5), fired[1]);
        Assert.Equal(T0.AddMinutes(15), fired[2]);
        Assert.Equal(T0.AddMinutes(35), fired[3]);
    }

    [Fact]
    public void Chronic_QuietPeriodResetsCooldownToBase()
    {
        var policy = new AlertPolicy(Defaults);
        policy.Decide(Stall("A"), T0);
        var secondToast = policy.Decide(Stall("A", 300), T0.AddSeconds(300));

        Assert.True(secondToast.Notify);

        // Quiet for more than twice the current interval (10 min), so the backoff resets.
        var resumeAt = 300 + 1201;
        var resumed = policy.Decide(Stall("A", resumeAt), T0.AddSeconds(resumeAt));

        Assert.True(resumed.Notify);

        // The next cooldown is back to the base 5 min, not the pre-reset 10 min.
        Assert.False(policy.Decide(Stall("A", resumeAt + 299), T0.AddSeconds(resumeAt + 299)).Notify);
        Assert.True(policy.Decide(Stall("A", resumeAt + 300), T0.AddSeconds(resumeAt + 300)).Notify);
    }
}
