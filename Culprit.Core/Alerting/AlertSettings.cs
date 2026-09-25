namespace Culprit.Core.Alerting;

public sealed record AlertSettings(TimeSpan Cooldown, bool Muted, bool NotifyUnattributed, bool WarnOnFirstIncident, TimeSpan RepeatWindow);

public sealed record AlertDecision(bool Notify, int SuppressedSinceLast);
