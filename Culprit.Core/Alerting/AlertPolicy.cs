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
/// </summary>
public sealed class AlertPolicy(AlertSettings settings)
{
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

        state.LastIncidentAt = now;

        var allowed = struckTwice
                      && !this.Settings.Muted
                      && (incident.InternalName is not null || this.Settings.NotifyUnattributed)
                      && (state.LastNotified is not { } last || now - last >= this.Settings.Cooldown);

        if (!allowed)
        {
            state.Suppressed++;
            return new AlertDecision(false, state.Suppressed);
        }

        var suppressed = state.Suppressed;
        state.Suppressed = 0;
        state.LastNotified = now;
        return new AlertDecision(true, suppressed);
    }

    public void Reset() => this.states.Clear();

    private sealed class State
    {
        public DateTime? LastNotified;
        public DateTime? LastIncidentAt;
        public int Suppressed;
    }
}
