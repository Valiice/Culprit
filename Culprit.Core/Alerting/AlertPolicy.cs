using Culprit.Core.Models;

namespace Culprit.Core.Alerting;

/// <summary>
/// Decides whether an incident becomes a toast. One cooldown per (plugin, rule); incidents
/// during a cooldown are counted and reported on the next toast so a chronic offender reads as
/// chronic instead of as a stream of identical messages.
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

        var allowed = !this.Settings.Muted
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
        public int Suppressed;
    }
}
