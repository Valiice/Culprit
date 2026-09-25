namespace Culprit.Core.Attribution;

/// <summary>
/// Maps exact Dalamud update-handler keys ("{Target}::{MethodName}") to the internal name of the
/// plugin that owns them. Same shape as TypeIndex, but keyed by the full handler key rather than
/// a type name, so it can attribute static handlers, lambdas, and shared-library handlers that
/// TypeIndex cannot tell apart. A key added under two different internal names is ambiguous and
/// resolves to null rather than silently picking whichever plugin was added last.
/// </summary>
public sealed class HandlerKeyMap
{
    private readonly Dictionary<string, string> map = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguous = new(StringComparer.Ordinal);

    public int Count => this.map.Count;

    public void Add(string handlerKey, string internalName)
    {
        if (this.ambiguous.Contains(handlerKey))
        {
            return;
        }

        if (this.map.TryGetValue(handlerKey, out var existing))
        {
            if (existing == internalName)
            {
                return;
            }

            this.map.Remove(handlerKey);
            this.ambiguous.Add(handlerKey);
            return;
        }

        this.map[handlerKey] = internalName;
    }

    public void Clear()
    {
        this.map.Clear();
        this.ambiguous.Clear();
    }

    /// <summary>True for a key the map has an answer for, including an ambiguous one (whose answer is null).</summary>
    public bool Contains(string key) => this.map.ContainsKey(key) || this.ambiguous.Contains(key);

    public string? Resolve(string handlerKey) =>
        this.ambiguous.Contains(handlerKey) ? null : this.map.GetValueOrDefault(handlerKey);
}
