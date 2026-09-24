namespace Culprit.Core.Attribution;

/// <summary>Maps type full names to the internal name of the plugin whose assembly defines them.</summary>
public sealed class TypeIndex
{
    private readonly Dictionary<string, string> map = new(StringComparer.Ordinal);

    public int Count => this.map.Count;

    public void Add(string typeFullName, string internalName) => this.map[typeFullName] = internalName;

    public void Clear() => this.map.Clear();

    public string? ResolveType(string typeFullName) => this.map.GetValueOrDefault(typeFullName);

    public string? ResolveHandlerKey(string handlerKey) =>
        HandlerKeyParser.TypeName(handlerKey) is { } typeName ? this.ResolveType(typeName) : null;
}
