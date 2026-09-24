namespace Culprit.Core.Alerting;

public sealed record AlertSettings(TimeSpan Cooldown, bool Muted, bool NotifyUnattributed);

public sealed record AlertDecision(bool Notify, int SuppressedSinceLast);
