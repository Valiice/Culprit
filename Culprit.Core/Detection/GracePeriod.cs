namespace Culprit.Core.Detection;

/// <summary>Quiet period after a plugin loads or unloads, because loading itself stalls.</summary>
public sealed class GracePeriod(TimeSpan length)
{
    private DateTime? changedAt;

    public void MarkChange(DateTime now) => this.changedAt = now;

    public bool IsActive(DateTime now) => this.changedAt is { } t && now - t < length;
}
