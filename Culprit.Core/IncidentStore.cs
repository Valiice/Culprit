using Culprit.Core.Models;

namespace Culprit.Core;

public sealed class PluginCounters
{
    public int Incidents { get; set; }

    public int Errors { get; set; }
}

/// <summary>In-memory ring of recent incidents plus per-plugin counters for the live table.</summary>
public sealed class IncidentStore
{
    public const int Capacity = 500;

    private readonly List<Incident> incidents = [];
    private readonly Dictionary<string, PluginCounters> counters = new(StringComparer.Ordinal);

    public int Count => this.incidents.Count;

    public void Add(Incident incident)
    {
        this.incidents.Add(incident);
        if (this.incidents.Count > Capacity)
        {
            this.incidents.RemoveAt(0);
        }

        if (incident.InternalName is { } name)
        {
            this.Counters(name).Incidents++;
        }
    }

    public void RecordError(string internalName) => this.Counters(internalName).Errors++;

    public IReadOnlyList<Incident> NewestFirst()
    {
        var copy = new List<Incident>(this.incidents);
        copy.Reverse();
        return copy;
    }

    public PluginCounters Counters(string internalName)
    {
        if (!this.counters.TryGetValue(internalName, out var c))
        {
            c = new PluginCounters();
            this.counters[internalName] = c;
        }

        return c;
    }

    public void Reset()
    {
        this.incidents.Clear();
        this.counters.Clear();
    }
}
