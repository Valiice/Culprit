using Culprit.Core.Models;
using Culprit.Core.Reporting;

namespace Culprit;

/// <summary>Builds the plain text stats report and the offender rows the Live tab shows.</summary>
internal sealed class ReportBuilder
{
    private readonly Configuration config;
    private readonly Sampler sampler;
    private readonly Roster roster;

    public ReportBuilder(Configuration config, Sampler sampler, Roster roster)
    {
        this.config = config;
        this.sampler = sampler;
        this.roster = roster;
    }

    public string ReportPath => Path.Combine(Services.PluginInterface.GetPluginConfigDirectory(), "culprit-report.txt");

    public IReadOnlyList<ReportPluginRow> Rows()
    {
        return this.roster.Plugins.Values
            .Where(p => p.InternalName != "Culprit")
            .Select(p =>
            {
                var window = this.sampler.Engine.Snapshot(p.InternalName);
                var counters = this.sampler.Store.Counters(p.InternalName);
                return new ReportPluginRow(
                    p.DisplayName,
                    p.InternalName,
                    window?.LastDrawMs ?? 0,
                    window?.LongestDrawMs ?? 0,
                    window?.LastUpdateMs ?? 0,
                    window?.LongestUpdateMs ?? 0,
                    window?.CurrentAverageMs ?? 0,
                    counters.Errors,
                    counters.Incidents,
                    this.sampler.Engine.Ignored.Contains(p.InternalName));
            })
            .OrderByDescending(r => r.Incidents)
            .ThenByDescending(r => r.AverageMs)
            .ToList();
    }

    public string Build()
    {
        var probes = this.sampler.ProbeStatuses
            .Select(s => new ReportProbe(s.Name, s.Available, s.Reason))
            .ToList();

        var incidents = this.sampler.Store.NewestFirst()
            .Select(i => new ReportIncident(
                i.Timestamp,
                i.InternalName is { } n ? this.roster.DisplayName(n) ?? n : "no plugin",
                RuleName(i.Rule),
                i.Value,
                i.Limit,
                i.Detail,
                i.Notified))
            .ToList();

        var context = new ReportContext(
            DateTime.UtcNow,
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version?.ToString() ?? "unknown",
            this.sampler.GcGen2Collections,
            this.sampler.GcTotalPauseMs,
            this.sampler.Paused,
            probes,
            this.sampler.Engine.Thresholds,
            this.Rows(),
            incidents,
            this.sampler.UnattributedHandlers.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            this.roster.HandlerMap.PluginCount,
            this.roster.HandlerMap.Count,
            this.roster.HandlerMap.AmbiguousKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());

        return StatsReport.Format(context);
    }

    public string Save()
    {
        var path = this.ReportPath;
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, this.Build());
        return path;
    }

    private static string RuleName(IncidentRule rule) => rule switch
    {
        IncidentRule.Stall => "Stall",
        IncidentRule.Sustained => "Sustained",
        IncidentRule.Errors => "Errors",
        IncidentRule.UnattributedStall => "Unattributed",
        _ => rule.ToString(),
    };
}
