using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace Culprit.Probes;

public sealed record DrawReading(string InternalName, double LastMs, double LongestMs);

/// <summary>
/// Reads each loaded plugin's PluginDrawStatistics (Dalamud internal). Turns on the global
/// UiBuilder.DoStats switch, which is off in release builds, and restores it on dispose.
/// Refresh() rebuilds the per-plugin cache; call it whenever the plugin set changes. Readings
/// are emitted only for frames the plugin actually drew, tracked via UiBuilder.FrameCount, so a
/// hidden UI, cutscene, gpose, or draw-time-tracking being off does not replay a stale reading.
/// </summary>
internal sealed class DrawStatsProbe : IDisposable
{
    private const string Name = "Draw stats";
    private const int MaxFailures = 3;

    private readonly Members? members;
    private readonly bool previousDoStats;
    private readonly bool switchedOn;
    private readonly List<(string InternalName, object Stats, Dalamud.Interface.UiBuilder UiBuilder)> cached = [];
    private readonly Dictionary<string, ulong> lastFrameCounts = new(StringComparer.Ordinal);
    private int failures;

    public DrawStatsProbe(DalamudInternals internals)
    {
        try
        {
            var pluginManager = internals.GetService("Dalamud.Plugin.Internal.PluginManager");
            var localPlugin = internals.GetType("Dalamud.Plugin.Internal.Types.LocalPlugin");
            var pluginInterface = internals.GetType("Dalamud.Plugin.DalamudPluginInterface");
            var uiBuilder = typeof(Dalamud.Interface.UiBuilder);
            var stats = internals.GetType("Dalamud.Interface.PluginDrawStatistics");

            this.members = new Members(
                pluginManager,
                DalamudInternals.Property(pluginManager.GetType(), "InstalledPlugins"),
                DalamudInternals.Property(localPlugin, "IsLoaded"),
                DalamudInternals.Property(localPlugin, "InternalName"),
                DalamudInternals.Property(localPlugin, "DalamudInterface"),
                DalamudInternals.Property(pluginInterface, "LocalUiBuilder"),
                DalamudInternals.Property(uiBuilder, "PluginDrawStatistics"),
                DalamudInternals.Property(uiBuilder, "DoStats"),
                DalamudInternals.Property(stats, "LastDrawTime"),
                DalamudInternals.Property(stats, "MaxDrawTime"));

            this.previousDoStats = (bool)(this.members.DoStats.GetValue(null) ?? false);
            this.members.DoStats.SetValue(null, true);
            this.switchedOn = true;
            this.Status = ProbeStatus.Ok(Name);
        }
        catch (Exception e)
        {
            this.members = null;
            this.Status = ProbeStatus.Failed(Name, e);
        }
    }

    public ProbeStatus Status { get; private set; }

    public void Refresh()
    {
        this.cached.Clear();
        this.lastFrameCounts.Clear();
        if (this.members is not { } m || !this.Status.Available)
        {
            return;
        }

        try
        {
            var plugins = (IEnumerable)(m.InstalledPlugins.GetValue(m.PluginManager) ?? Array.Empty<object>());
            foreach (var plugin in plugins)
            {
                if (m.IsLoaded.GetValue(plugin) is not true)
                {
                    continue;
                }

                var dalamudInterface = m.DalamudInterface.GetValue(plugin);
                var uiBuilder = dalamudInterface is null ? null : m.LocalUiBuilder.GetValue(dalamudInterface);
                var stats = uiBuilder is null ? null : m.PluginDrawStatistics.GetValue(uiBuilder);
                if (stats is null
                    || uiBuilder is not Dalamud.Interface.UiBuilder typedUiBuilder
                    || m.InternalName.GetValue(plugin) is not string internalName)
                {
                    continue;
                }

                this.cached.Add((internalName, stats, typedUiBuilder));
            }
        }
        catch (Exception e)
        {
            this.Fail(e);
        }
    }

    /// <summary>
    /// Last and longest draw time per loaded plugin, in ms. Plugins that never drew, or whose
    /// UiBuilder.FrameCount has not advanced since the previous call, are skipped.
    /// </summary>
    public IReadOnlyList<DrawReading> Read()
    {
        if (this.members is not { } m || !this.Status.Available)
        {
            return [];
        }

        var result = new List<DrawReading>(this.cached.Count);
        try
        {
            foreach (var (internalName, stats, uiBuilder) in this.cached)
            {
                var frameCount = uiBuilder.FrameCount;
                var hasLastFrameCount = this.lastFrameCounts.TryGetValue(internalName, out var lastFrameCount);
                this.lastFrameCounts[internalName] = frameCount;
                if (hasLastFrameCount && frameCount == lastFrameCount)
                {
                    continue;
                }

                var last = (long)(m.LastDrawTime.GetValue(stats) ?? -1L);
                if (last < 0)
                {
                    continue;
                }

                var longest = (long)(m.MaxDrawTime.GetValue(stats) ?? -1L);
                result.Add(new DrawReading(internalName, TicksToMs(last), TicksToMs(Math.Max(longest, 0))));
            }

            this.failures = 0;
        }
        catch (Exception e)
        {
            this.Fail(e);
        }

        return result;
    }

    public void Dispose()
    {
        if (!this.switchedOn)
        {
            return;
        }

        try
        {
            this.members?.DoStats.SetValue(null, this.previousDoStats);
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit could not restore UiBuilder.DoStats");
        }
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void Fail(Exception e)
    {
        if (++this.failures >= MaxFailures)
        {
            this.Status = ProbeStatus.Failed(Name, e);
            Services.Log.Warning(e, "Culprit draw stats probe disabled after repeated failures");
        }
    }

    private sealed record Members(
        object PluginManager,
        PropertyInfo InstalledPlugins,
        PropertyInfo IsLoaded,
        PropertyInfo InternalName,
        PropertyInfo DalamudInterface,
        PropertyInfo LocalUiBuilder,
        PropertyInfo PluginDrawStatistics,
        PropertyInfo DoStats,
        PropertyInfo LastDrawTime,
        PropertyInfo MaxDrawTime);
}
