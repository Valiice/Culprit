namespace Culprit.Core.Attribution;

/// <summary>
/// Dalamud keys update-handler timings as "{Target}::{MethodName}". Target is the handler's
/// object, whose default ToString is its type full name. Static handlers have no target.
/// </summary>
public static class HandlerKeyParser
{
    public static string? TypeName(string handlerKey)
    {
        var separator = handlerKey.IndexOf("::", StringComparison.Ordinal);
        return separator <= 0 ? null : handlerKey[..separator];
    }
}
