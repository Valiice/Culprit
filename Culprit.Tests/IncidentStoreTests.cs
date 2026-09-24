using Culprit.Core;
using Culprit.Core.Models;
using Xunit;

namespace Culprit.Tests;

public class IncidentStoreTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Incident Stall(string? plugin, int n) =>
        new(T0.AddSeconds(n), plugin, IncidentRule.Stall, n, 20, "Draw");

    [Fact]
    public void NewestFirst_ReturnsReverseInsertionOrder()
    {
        var store = new IncidentStore();
        store.Add(Stall("A", 1));
        store.Add(Stall("A", 2));

        var list = store.NewestFirst();

        Assert.Equal(2, list[0].Value);
        Assert.Equal(1, list[1].Value);
    }

    [Fact]
    public void CapsAtCapacity_DroppingOldest()
    {
        var store = new IncidentStore();
        for (var i = 0; i < IncidentStore.Capacity + 10; i++)
        {
            store.Add(Stall("A", i));
        }

        Assert.Equal(IncidentStore.Capacity, store.Count);
        Assert.Equal(IncidentStore.Capacity + 9, store.NewestFirst()[0].Value);
        Assert.Equal(10, store.NewestFirst()[^1].Value);
    }

    [Fact]
    public void Counters_CountIncidentsPerPluginAndErrorsSeparately()
    {
        var store = new IncidentStore();
        store.Add(Stall("A", 1));
        store.Add(Stall("A", 2));
        store.Add(Stall("B", 3));
        store.Add(Stall(null, 4));
        store.RecordError("A");
        store.RecordError("A");
        store.RecordError("A");

        Assert.Equal(2, store.Counters("A").Incidents);
        Assert.Equal(3, store.Counters("A").Errors);
        Assert.Equal(1, store.Counters("B").Incidents);
        Assert.Equal(0, store.Counters("B").Errors);
        Assert.Equal(0, store.Counters("Unknown").Incidents);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var store = new IncidentStore();
        store.Add(Stall("A", 1));
        store.RecordError("A");

        store.Reset();

        Assert.Equal(0, store.Count);
        Assert.Empty(store.NewestFirst());
        Assert.Equal(0, store.Counters("A").Incidents);
        Assert.Equal(0, store.Counters("A").Errors);
    }
}
