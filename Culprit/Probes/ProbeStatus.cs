namespace Culprit.Probes;

/// <summary>Result of a probe's self-check. Reason is the exact failure, meant to be pasted into an issue.</summary>
public sealed record ProbeStatus(string Name, bool Available, string? Reason)
{
    public static ProbeStatus Ok(string name) => new(name, true, null);

    public static ProbeStatus Failed(string name, Exception e) => new(name, false, $"{e.GetType().Name}: {e.Message}");
}
