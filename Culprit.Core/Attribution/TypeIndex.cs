namespace Culprit.Core.Attribution;

/// <summary>
/// Maps type full names to the internal name of the plugin whose assembly defines them.
/// A type name that gets added under two different internal names (as happens with shared
/// library types compiled into multiple plugins, such as ECommons) is ambiguous and resolves
/// to null rather than silently picking whichever plugin was indexed last.
/// </summary>
public sealed class TypeIndex
{
    private readonly Dictionary<string, string> map = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguous = new(StringComparer.Ordinal);

    public int Count => this.map.Count;

    public void Add(string typeFullName, string internalName)
    {
        if (this.ambiguous.Contains(typeFullName))
        {
            return;
        }

        if (this.map.TryGetValue(typeFullName, out var existing))
        {
            if (existing == internalName)
            {
                return;
            }

            this.map.Remove(typeFullName);
            this.ambiguous.Add(typeFullName);
            return;
        }

        this.map[typeFullName] = internalName;
    }

    public void Clear()
    {
        this.map.Clear();
        this.ambiguous.Clear();
    }

    public string? ResolveType(string typeFullName) =>
        this.ambiguous.Contains(typeFullName) ? null : this.map.GetValueOrDefault(typeFullName);

    public string? ResolveHandlerKey(string handlerKey) =>
        HandlerKeyParser.TypeName(handlerKey) is { } typeName ? this.ResolveType(typeName) : null;
}
