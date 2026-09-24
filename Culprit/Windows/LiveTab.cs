using Dalamud.Bindings.ImGui;

namespace Culprit.Windows;

internal sealed class LiveTab
{
    private readonly Configuration config;
    private readonly Sampler sampler;
    private readonly Roster roster;

    public LiveTab(Configuration config, Sampler sampler, Roster roster)
    {
        this.config = config;
        this.sampler = sampler;
        this.roster = roster;
    }

    public void Draw()
    {
        this.DrawStatus();
        ImGui.Separator();
        this.DrawTable();
    }

    private void DrawStatus()
    {
        if (this.sampler.Paused)
        {
            ImGui.TextColored(Colors.Warn, "Paused");
            ImGui.SameLine();
            if (ImGui.SmallButton("Resume"))
            {
                this.sampler.Paused = false;
            }
        }
        else
        {
            ImGui.TextColored(Colors.Good, "Running");
            ImGui.SameLine();
            if (ImGui.SmallButton("Pause"))
            {
                this.sampler.Paused = true;
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Reset counters"))
        {
            this.sampler.Reset();
        }

        foreach (var status in this.sampler.ProbeStatuses)
        {
            if (status.Available)
            {
                ImGui.TextColored(Colors.Good, $"{status.Name}: available");
            }
            else
            {
                ImGui.TextColored(Colors.Bad, $"{status.Name}: unavailable");
                ImGui.SameLine();
                ImGui.TextDisabled(status.Reason ?? string.Empty);
            }
        }

        if (this.sampler.ReducedMode)
        {
            ImGui.TextColored(Colors.Warn, "Reduced mode: some signals are missing. Dalamud's internals may have changed; report the red line above.");
        }

        foreach (var warning in this.sampler.TrackingWarnings)
        {
            ImGui.TextColored(Colors.Warn, warning);
        }
    }

    private void DrawTable()
    {
        var rows = this.roster.Plugins.Values
            .Where(p => p.InternalName != "Culprit")
            .Select(p => (Info: p, Window: this.sampler.Engine.Snapshot(p.InternalName), Counters: this.sampler.Store.Counters(p.InternalName)))
            .OrderByDescending(r => r.Counters.Incidents)
            .ThenByDescending(r => r.Window?.CurrentAverageMs ?? 0)
            .ToList();

        if (!ImGui.BeginTable("##live", 9, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Draw last", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Draw max", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Update last", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Update max", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("5 s avg", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Errors", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Incidents", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableSetupColumn("Ignore", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        var stall = this.sampler.Engine.Thresholds.StallMs;
        foreach (var (info, window, counters) in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var ignored = this.sampler.Engine.Ignored.Contains(info.InternalName);
            if (ignored)
            {
                ImGui.TextColored(Colors.Dim, info.DisplayName);
            }
            else
            {
                ImGui.TextUnformatted(info.DisplayName);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(info.InternalName + (info.IsDev ? " (dev plugin)" : string.Empty));
            }

            Ms(window?.LastDrawMs, stall);
            Ms(window?.LongestDrawMs, stall);
            Ms(window?.LastUpdateMs, stall);
            Ms(window?.LongestUpdateMs, stall);
            Ms(window?.CurrentAverageMs, this.sampler.Engine.Thresholds.SustainedMs);

            ImGui.TableNextColumn();
            Count(counters.Errors);
            ImGui.TableNextColumn();
            Count(counters.Incidents);

            ImGui.TableNextColumn();
            var ignore = ignored;
            if (ImGui.Checkbox($"##ignore{info.InternalName}", ref ignore))
            {
                if (ignore)
                {
                    this.config.IgnoredPlugins.Add(info.InternalName);
                }
                else
                {
                    this.config.IgnoredPlugins.RemoveAll(n => string.Equals(n, info.InternalName, StringComparison.OrdinalIgnoreCase));
                }

                this.config.Save();
                this.sampler.ApplyConfig();
            }
        }

        ImGui.EndTable();
    }

    private static void Ms(double? value, double limit)
    {
        ImGui.TableNextColumn();
        if (value is not { } v)
        {
            ImGui.TextDisabled("-");
            return;
        }

        var text = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (v > limit)
        {
            ImGui.TextColored(Colors.Bad, text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }

    private static void Count(int value)
    {
        if (value == 0)
        {
            ImGui.TextDisabled("0");
        }
        else
        {
            ImGui.TextColored(Colors.Warn, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
