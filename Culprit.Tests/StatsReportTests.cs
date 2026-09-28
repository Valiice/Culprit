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
        IReadOnlyList<ReportIncident>? incidents = null,
        IReadOnlyList<string>? unattributedHandlers = null,
        int gcGen2Collections = 0,
        double gcTotalPauseMs = 0,
        int handlerMapPlugins = 0,
        int handlerMapKeys = 0,
        IReadOnlyList<string>? ambiguousHandlers = null) =>
        new(
            T0,
            "1.2.3",
            "9.0.0",
            gcGen2Collections,
            gcTotalPauseMs,
            paused,
            probes ?? [],
            new Thresholds(),
            plugins ?? [],
            incidents ?? [],
            unattributedHandlers ?? [],
            handlerMapPlugins,
            handlerMapKeys,
            ambiguousHandlers ?? []);

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
    public void GcLine_ReportsCollectionsAndTotalPause()
    {
        var text = StatsReport.Format(Context(gcGen2Collections: 4, gcTotalPauseMs: 812.3));

        Assert.Contains("GC: 4 gen2 collections, 812 ms total pause since the process started", text);
    }

    [Fact]
    public void Format_ListsUnattributedHandlers()
    {
        var text = StatsReport.Format(Context(
            unattributedHandlers: ["InventoryTools.Services.OverlayService::FrameworkOnUpdate", "Other.Type::OnUpdate"],
            handlerMapPlugins: 12,
            handlerMapKeys: 34));

        Assert.Contains("Unattributed update handlers (2; handler map knows 12 plugins and 34 keys)", text);
        var lines = text.Split('\n');
        Assert.Contains(lines, l => l == "  InventoryTools.Services.OverlayService::FrameworkOnUpdate");
        Assert.Contains(lines, l => l == "  Other.Type::OnUpdate");
    }

    [Fact]
    public void Format_NoUnattributedHandlers_PrintsNone()
    {
        var text = StatsReport.Format(Context());

        Assert.Contains("Unattributed update handlers (0; handler map knows 0 plugins and 0 keys)\n  none", text);
    }

    [Fact]
    public void Format_ListsAmbiguousHandlers()
    {
        var text = StatsReport.Format(Context(ambiguousHandlers: ["Shared.Type::OnUpdate"]));

        Assert.Contains("Ambiguous update handlers (1)", text);
        var lines = text.Split('\n');
        Assert.Contains(lines, l => l == "  Shared.Type::OnUpdate");
    }

    [Fact]
    public void Format_NoAmbiguousHandlers_PrintsNone()
    {
        var text = StatsReport.Format(Context());

        Assert.Contains("Ambiguous update handlers (0)\n  none", text);
    }

    [Fact]
    public void ThresholdsLine_ForDefaultThresholds_IsExact()
    {
        var text = StatsReport.Format(Context());

        var line = text.Split('\n').Single(l => l.StartsWith("  Stall", StringComparison.Ordinal));
        Assert.Equal("  Stall 40 ms, Sustained 3.0 ms over 5 s, Errors 5 in 60 s, Unattributed 100 ms", line);
    }
}
