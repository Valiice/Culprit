using System.Numerics;

namespace Culprit.Windows;

internal static class Colors
{
    public static readonly Vector4 Good = new(0.2f, 0.85f, 0.2f, 1f);
    public static readonly Vector4 Bad = new(0.95f, 0.25f, 0.25f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.6f, 0.15f, 1f);
    public static readonly Vector4 Dim = new(0.5f, 0.5f, 0.5f, 1f);
}
