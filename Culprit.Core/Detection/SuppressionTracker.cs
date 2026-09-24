namespace Culprit.Core.Detection;

/// <summary>
/// Decides whether this tick is a fair moment to judge plugins. Blocked while the game says so
/// (zoning, cutscene), for a grace period after that block clears (the game keeps streaming the
/// area for a few seconds), and for a grace period after any plugin loads or unloads.
/// </summary>
public sealed class SuppressionTracker(TimeSpan grace)
{
    private readonly GracePeriod pluginGrace = new(grace);
    private readonly GracePeriod blockGrace = new(grace);
    private bool wasBlocked;

    public void MarkPluginChange(DateTime now) => this.pluginGrace.MarkChange(now);

    /// <summary>True when incidents must not be emitted this tick.</summary>
    public bool Evaluate(bool blockedNow, DateTime now)
    {
        if (blockedNow)
        {
            this.wasBlocked = true;
            return true;
        }

        if (this.wasBlocked)
        {
            this.wasBlocked = false;
            this.blockGrace.MarkChange(now);
        }

        return this.pluginGrace.IsActive(now) || this.blockGrace.IsActive(now);
    }
}
