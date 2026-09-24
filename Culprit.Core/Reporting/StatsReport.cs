using System.Globalization;

namespace Culprit.Core.Reporting;

public sealed record ReportProbe(string Name, bool Available, string? Reason);

public sealed record ReportPluginRow(
    string DisplayName, string InternalName,
    double LastDrawMs, double LongestDrawMs, double LastUpdateMs, double LongestUpdateMs,
    double AverageMs, int Errors, int Incidents, bool Ignored);

public sealed record ReportIncident(
    DateTime Timestamp, string PluginDisplayName, string Rule, double Value, double Limit, string Detail, bool Notified);

public sealed record ReportContext(
    DateTime GeneratedUtc, string CulpritVersion, string DalamudVersion, bool Paused,
    IReadOnlyList<ReportProbe> Probes, Thresholds Thresholds,
    IReadOnlyList<ReportPluginRow> Plugins, IReadOnlyList<ReportIncident> Incidents);

/// <summary>Renders a plain text stats report for sharing or for a tool to read from disk.</summary>
public static class StatsReport
{
    public const int MaxIncidents = 50;

    public static string Format(ReportContext context)
    {
        var ci = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "Culprit report",
            $"Generated: {context.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss", ci)} UTC",
            $"Culprit {context.CulpritVersion}, Dalamud {context.DalamudVersion}",
            $"Detection: {(context.Paused ? "paused" : "running")}",
            string.Empty,
            "Probes",
        };

        foreach (var probe in context.Probes)
        {
            var name = Clean(probe.Name);
            lines.Add(probe.Available
                ? $"  {name}: available"
                : probe.Reason is { } reason
                    ? $"  {name}: unavailable, {Clean(reason)}"
                    : $"  {name}: unavailable");
        }

        var t = context.Thresholds;
        lines.Add(string.Empty);
        lines.Add("Thresholds");
        lines.Add(
            $"  Stall {t.StallMs.ToString("0", ci)} ms, "
            + $"Sustained {t.SustainedMs.ToString("0.0", ci)} ms over {t.SustainedWindowSeconds.ToString("0", ci)} s, "
            + $"Errors {t.ErrorCount.ToString(ci)} in {t.ErrorWindowSeconds.ToString("0", ci)} s, "
            + $"Unattributed {t.UnattributedMs.ToString("0", ci)} ms");

        lines.Add(string.Empty);
        lines.Add($"Plugins ({context.Plugins.Count.ToString(ci)}, sorted by incidents then 5 s average)");
        lines.Add("  Plugin | Internal | Draw last | Draw max | Update last | Update max | 5 s avg | Errors | Incidents | Ignored");
        foreach (var p in context.Plugins)
        {
            lines.Add(
                $"  {Clean(p.DisplayName)} | {Clean(p.InternalName)} | {p.LastDrawMs.ToString("0.0", ci)} | {p.LongestDrawMs.ToString("0.0", ci)} | "
                + $"{p.LastUpdateMs.ToString("0.0", ci)} | {p.LongestUpdateMs.ToString("0.0", ci)} | {p.AverageMs.ToString("0.0", ci)} | "
                + $"{p.Errors.ToString(ci)} | {p.Incidents.ToString(ci)} | {(p.Ignored ? "yes" : "no")}");
        }

        var total = context.Incidents.Count;
        var shown = Math.Min(total, MaxIncidents);
        lines.Add(string.Empty);
        lines.Add($"Incidents (newest first, showing {shown.ToString(ci)} of {total.ToString(ci)})");
        if (total == 0)
        {
            lines.Add("  none");
        }
        else
        {
            lines.Add("  Time | Plugin | Rule | Value | Limit | Detail | Toast");
            foreach (var incident in context.Incidents.Take(MaxIncidents))
            {
                lines.Add(
                    $"  {incident.Timestamp.ToLocalTime().ToString("HH:mm:ss", ci)} | {Clean(incident.PluginDisplayName)} | {Clean(incident.Rule)} | "
                    + $"{incident.Value.ToString("0.0", ci)} | {incident.Limit.ToString("0", ci)} | {Clean(incident.Detail)} | "
                    + $"{(incident.Notified ? "yes" : "no")}");
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Keeps free text from breaking the pipe-delimited row structure: newlines become spaces,
    /// runs of whitespace collapse to one space, pipes become slashes, and the result is trimmed.
    /// </summary>
    private static string Clean(string value)
    {
        var withoutBreaksOrPipes = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        var parts = withoutBreaksOrPipes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
