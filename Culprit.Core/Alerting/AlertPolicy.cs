using Culprit.Core.Models;

namespace Culprit.Core.Alerting;

/// <summary>
/// Decides whether an incident becomes a toast. One cooldown per (plugin, rule); incidents
/// during a cooldown are counted and reported on the next toast so a chronic offender reads as
/// chronic instead of as a stream of identical messages. For Stall and Sustained, when
/// WarnOnFirstIncident is off, a second strike is required: an incident only notifies if the
/// same (plugin, rule) had a previous incident within RepeatWindow, so a one-off cost such as a
/// plugin opening a window for the first time stays quiet. Errors and UnattributedStall are
/// unaffected, since Errors already needs several events and unattributed has its own toggle.
/// The cooldown itself backs off for a chronic offender: it doubles after each toast for the
/// same (plugin, rule) while incidents keep arriving, starting at Settings.Cooldown and capped
/// at 60 minutes, and resets to Settings.Cooldown once that key goes quiet for twice the current
/// interval, so a permanently broken plugin is toasted less and less often instead of at a
/// constant rate.
/// </summary>
public sealed class AlertPolicy(AlertSettings settings)
{
    /// <summary>Cap on the per-(plugin, rule) cooldown backoff (60 minutes).</summary>
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(60);

    private readonly Dictionary<(string Plugin, IncidentRule Rule), State> states = [];

    public AlertSettings Settings { get; set; } = settings;

    public AlertDecision Decide(Incident incident, DateTime now)
    {
        var key = (incident.InternalName ?? string.Empty, incident.Rule);
        if (!this.states.TryGetValue(key, out var state))
        {
            state = new State();
            this.states[key] = state;
        }

        var struckTwice = this.Settings.WarnOnFirstIncident
                           || (incident.Rule != IncidentRule.Stall && incident.Rule != IncidentRule.Sustained)
                           || (state.LastIncidentAt is { } lastIncident && now - lastIncident <= this.Settings.RepeatWindow);

        var previousIncidentAt = state.LastIncidentAt;
        state.LastIncidentAt = now;

        var cooldown = state.Cooldown > TimeSpan.Zero ? state.Cooldown : this.Settings.Cooldown;

        // A key that has been quiet for twice its current backoff interval is no longer chronic:
        // the next toast, if any, starts the backoff over from the base cooldown.
        if (previousIncidentAt is { } prev && now - prev >= cooldown + cooldown)
        {
            state.Cooldown = TimeSpan.Zero;
            cooldown = this.Settings.Cooldown;
        }

        var allowed = struckTwice
                      && !this.Settings.Muted
                      && (incident.InternalName is not null || this.Settings.NotifyUnattributed)
                      && (state.LastNotified is not { } last || now - last >= cooldown);

        if (!allowed)
        {
            state.Suppressed++;
            return new AlertDecision(false, state.Suppressed);
        }

        var suppressed = state.Suppressed;
        state.Suppressed = 0;
        state.LastNotified = now;
        state.Cooldown = state.Cooldown > TimeSpan.Zero
            ? (state.Cooldown * 2 > MaxCooldown ? MaxCooldown : state.Cooldown * 2)
            : this.Settings.Cooldown;

        return new AlertDecision(true, suppressed);
    }

    public void Reset() => this.states.Clear();

    private sealed class State
    {
        public DateTime? LastNotified;
        public DateTime? LastIncidentAt;
        public int Suppressed;
        public TimeSpan Cooldown;
    }
}
