namespace Culprit.Core.Attribution;

/// <summary>
/// Resolves an update-handler key to a plugin's internal name, preferring the exact key map
/// (built from Dalamud's own subscription lists, so it covers static handlers, lambdas, and
/// shared-library handlers) and falling back to the type-name index when the map has no answer
/// for that key. An ambiguous exact match in the map resolves to null rather than falling
/// through to the type index, since the map's ambiguity is the more specific answer.
/// </summary>
public sealed class HandlerResolver
{
    /// <summary>Pseudo plugin internal name used for handlers that belong to Dalamud itself.</summary>
    public const string DalamudInternalName = "Dalamud";

    private readonly HandlerKeyMap map;
    private readonly TypeIndex typeIndex;

    public HandlerResolver(HandlerKeyMap map, TypeIndex typeIndex)
    {
        this.map = map;
        this.typeIndex = typeIndex;
    }

    public string? Resolve(string handlerKey) =>
        this.map.Contains(handlerKey) ? this.map.Resolve(handlerKey) : this.typeIndex.ResolveHandlerKey(handlerKey);
}
