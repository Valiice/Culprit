using Culprit.Core;
using Culprit.Core.Reporting;
using Xunit;

namespace Culprit.Tests;

public class StatsReportTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 21, 55, 12, DateTimeKind.Utc);

    private static ReportContext Context(
        bool paused = false,
        IReadOnlyList<ReportProbe>? probes = null,
        IReadOnlyList<ReportPluginRow>? plugins = null,
        IReadOnlyList<ReportIncident>? incidents = null) =>
        new(
            T0,
            "1.2.3",
            "9.0.0",
            paused,
            probes ?? [],
            new Thresholds(),
            plugins ?? [],
            incidents ?? []);

    [Fact]
    public void Header_ContainsBothVersionsAndPausedState()
    {
        var text = StatsReport.Format(Context(paused: true));

        Assert.Contains("Culprit 1.2.3, Dalamud 9.0.0", text);
        Assert.Contains("Detection: paused", text);
    }

    [Fact]
    public void Header_ShowsRunningWhenNotPaused()
    {
        var text = StatsReport.Format(Context(paused: false));

        Assert.Contains("Detection: running", text);
    }

    [Fact]
    public void UnavailableProbe_LineContainsReason()
    {
        var probes = new List<ReportProbe> { new("Draw stats", false, "NullReferenceException: boom") };

        var text = StatsReport.Format(Context(probes: probes));

        Assert.Contains("Draw stats: unavailable, NullReferenceException: boom", text);
    }

    [Fact]
    public void PluginRow_RendersOneDecimalMsIntegerCountsAndIgnoredFlag()
    {
        var plugins = new List<ReportPluginRow>
        {
            new("Peeping Tom", "PeepingTom", 0.3, 8.2, 0.1, 11.9, 0.4, 0, 0, true),
        };

        var text = StatsReport.Format(Context(plugins: plugins));

        Assert.Contains("Peeping Tom | PeepingTom | 0.3 | 8.2 | 0.1 | 11.9 | 0.4 | 0 | 0 | yes", text);
    }

    [Fact]
    public void Format_CleansNewlinesAndPipesInFreeText()
    {
        var incidents = new List<ReportIncident>
        {
            new(T0, "Peeping Tom", "Stall", 48.0, 20, "line one\nline two | more", true),
        };

        var text = StatsReport.Format(Context(incidents: incidents));

        var lines = text.Split('\n');
        var incidentRowLines = lines.Count(l => l.Contains("Peeping Tom | Stall"));
        Assert.Equal(1, incidentRowLines);
        Assert.Contains(lines, l => l.Contains("line one line two / more"));
    }

    [Fact]
    public void Incidents_BeyondFiftyAreCutAndHeaderReportsShownOfTotal()
    {
        var incidents = new List<ReportIncident>();
        for (var i = 0; i < 60; i++)
        {
            incidents.Add(new ReportIncident(T0.AddSeconds(i), "Peeping Tom", "Stall", 48.0, 20, "Draw", true));
        }

        var text = StatsReport.Format(Context(incidents: incidents));

        Assert.Contains("showing 50 of 60", text);
        var lines = text.Split('\n');
        var incidentRowLines = lines.Count(l => l.Contains("Peeping Tom | Stall"));
        Assert.Equal(StatsReport.MaxIncidents, incidentRowLines);
    }

    [Fact]
    public void EmptyIncidentList_PrintsNone()
    {
        var text = StatsReport.Format(Context());

        Assert.Contains("showing 0 of 0", text);
        Assert.Contains("\n  none", text);
    }

    [Fact]
    public void ThresholdsLine_ForDefaultThresholds_IsExact()
    {
        var text = StatsReport.Format(Context());

        var line = text.Split('\n').Single(l => l.StartsWith("  Stall", StringComparison.Ordinal));
        Assert.Equal("  Stall 20 ms, Sustained 3.0 ms over 5 s, Errors 5 in 60 s, Unattributed 100 ms", line);
    }
}
