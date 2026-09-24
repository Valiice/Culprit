using Culprit.Core;
using Culprit.Core.Alerting;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class ToastFormatterTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Thresholds T = new();

    [Fact]
    public void Title_UsesDisplayNameOrFallback()
    {
        var i = new Incident(T0, "PlatePeek", IncidentRule.Stall, 48, 20, "Draw");

        Assert.Equal("Plate Peek", ToastFormatter.Title(i, "Plate Peek"));
        Assert.Equal("PlatePeek", ToastFormatter.Title(i, null));
        Assert.Equal("No plugin", ToastFormatter.Title(i with { InternalName = null }, null));
    }

    [Fact]
    public void Body_Stall()
    {
        var i = new Incident(T0, "P", IncidentRule.Stall, 48.4, 20, "Draw");

        Assert.Equal("Draw took 48 ms (limit 20 ms)", ToastFormatter.Body(i, T, 0));
    }

    [Fact]
    public void Body_Sustained()
    {
        var i = new Incident(T0, "P", IncidentRule.Sustained, 4.12, 3, "5 s window");

        Assert.Equal("Averaged 4.1 ms per frame over the last 5 s (limit 3 ms)", ToastFormatter.Body(i, T, 0));
    }

    [Fact]
    public void Body_Errors()
    {
        var i = new Incident(T0, "P", IncidentRule.Errors, 7, 5, "boom");

        Assert.Equal("7 errors in the last 60 s", ToastFormatter.Body(i, T, 0));
    }

    [Fact]
    public void Body_Unattributed()
    {
        var i = new Incident(T0, null, IncidentRule.UnattributedStall, 150.7, 100, "Frame");

        Assert.Equal("Frame took 151 ms with no plugin over 20 ms", ToastFormatter.Body(i, T, 0));
    }

    [Fact]
    public void Body_AppendsSuppressedCount()
    {
        var i = new Incident(T0, "P", IncidentRule.Stall, 48, 20, "Update");

        Assert.Equal("Update took 48 ms (limit 20 ms), 12 more since the last warning", ToastFormatter.Body(i, T, 12));
    }
}
