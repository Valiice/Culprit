namespace Culprit.Core.Sampling;

/// <summary>
/// Dalamud appends one sample per handler invocation to a per-key list and caps it at 1000,
/// after which the count stops changing and a stale key is indistinguishable from a live one.
/// This tracks the last seen count per key, and says when to trim a list so it never reaches
/// the cap. Trimming keeps the newest TrimTo entries, enough for the /xlstats averages.
/// </summary>
public sealed class FreshnessTracker
{
    public const int TrimAbove = 500;
    public const int TrimTo = 100;

    private readonly Dictionary<string, int> lastCounts = new(StringComparer.Ordinal);

    public static bool ShouldTrim(int count) => count > TrimAbove;

    /// <summary>True when the key is unseen or its list grew since the last Record.</summary>
    public bool IsNew(string key, int count) =>
        !this.lastCounts.TryGetValue(key, out var last) || count > last;

    public void Record(string key, int count) => this.lastCounts[key] = count;

    public void Forget(string key) => this.lastCounts.Remove(key);
}
