namespace Culprit.Core.Attribution;

public enum HitchKind
{
    Draw,
    Update,
    Frame,
    Unknown,
}

/// <summary>Kind of Dalamud hitch and what it points at: a plugin display name, a handler key, or nothing.</summary>
public sealed record HitchName(HitchKind Kind, string Target);

/// <summary>
/// Dalamud's "[HITCH] Long {Name} detected" warnings carry a Name of the form
/// "UiBuilder(Display Name)" for draw, "Type::Method" for update handlers, or "FrameworkUpdate".
/// </summary>
public static class HitchNameParser
{
    private const string DrawPrefix = "UiBuilder(";

    public static HitchName Parse(string name)
    {
        if (name.StartsWith(DrawPrefix, StringComparison.Ordinal) && name.EndsWith(')'))
        {
            return new HitchName(HitchKind.Draw, name[DrawPrefix.Length..^1]);
        }

        if (name.Contains("::", StringComparison.Ordinal))
        {
            return new HitchName(HitchKind.Update, name);
        }

        if (name == "FrameworkUpdate")
        {
            return new HitchName(HitchKind.Frame, name);
        }

        return new HitchName(HitchKind.Unknown, name);
    }
}
