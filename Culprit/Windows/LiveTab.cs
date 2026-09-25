using Dalamud.Bindings.ImGui;

namespace Culprit.Windows;

internal sealed class LiveTab
{
    private readonly Configuration config;
    private readonly Sampler sampler;
    private readonly Roster roster;
    private readonly ReportBuilder reportBuilder;
    private DateTime? savedAt;
    private string? savedPath;
    private string? saveError;

    public LiveTab(Configuration config, Sampler sampler, Roster roster, ReportBuilder reportBuilder)
    {
        this.config = config;
        this.sampler = sampler;
        this.roster = roster;
        this.reportBuilder = reportBuilder;
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

        ImGui.SameLine();
        if (ImGui.SmallButton("Copy report"))
        {
            try
            {
                ImGui.SetClipboardText(this.reportBuilder.Build());
            }
            catch (Exception e)
            {
                Services.Log.Error(e, "Culprit could not build the report");
                this.saveError = e.Message;
                this.savedAt = DateTime.UtcNow;
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Save report"))
        {
            try
            {
                this.savedPath = this.reportBuilder.Save();
                this.savedAt = DateTime.UtcNow;
                this.saveError = null;
            }
            catch (Exception e)
            {
                Services.Log.Error(e, "Culprit could not save the report");
                this.saveError = e.Message;
                this.savedAt = DateTime.UtcNow;
            }
        }

        if (this.savedAt is { } savedAt && DateTime.UtcNow - savedAt < TimeSpan.FromSeconds(5))
        {
            if (this.saveError is { } error)
            {
                ImGui.TextColored(Colors.Bad, $"Could not save the report: {error}");
            }
            else
            {
                ImGui.TextDisabled($"Saved to {this.savedPath}");
            }
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

        if (this.roster.Indexing)
        {
            ImGui.TextDisabled("Attribution: indexing plugin assemblies");
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
        var showAll = this.config.ShowAllPlugins;
        if (ImGui.Checkbox("Show all plugins", ref showAll))
        {
            this.config.ShowAllPlugins = showAll;
            this.config.Save();
        }

        if (!showAll)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Only plugins with an incident, an error, or a 5 s average of 1 ms or more are listed.");
        }

        var rows = this.reportBuilder.Rows();
        if (!showAll)
        {
            rows = rows.Where(r => r.Incidents > 0 || r.Errors > 0 || r.AverageMs >= 1.0).ToList();
        }

        if (rows.Count == 0)
        {
            ImGui.TextColored(Colors.Good, "No plugin has caused a hiccup yet.");
            return;
        }

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
        var sustained = this.sampler.Engine.Thresholds.SustainedMs;
        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (row.Ignored)
            {
                ImGui.TextColored(Colors.Dim, row.DisplayName);
            }
            else
            {
                ImGui.TextUnformatted(row.DisplayName);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(row.InternalName);
            }

            Ms(row.LastDrawMs, stall);
            Ms(row.LongestDrawMs, stall);
            Ms(row.LastUpdateMs, stall);
            Ms(row.LongestUpdateMs, stall);
            Ms(row.AverageMs, sustained);

            ImGui.TableNextColumn();
            Count(row.Errors);
            ImGui.TableNextColumn();
            Count(row.Incidents);

            ImGui.TableNextColumn();
            var ignore = row.Ignored;
            if (ImGui.Checkbox($"##ignore{row.InternalName}", ref ignore))
            {
                if (ignore)
                {
                    this.config.IgnoredPlugins.Add(row.InternalName);
                }
                else
                {
                    this.config.IgnoredPlugins.RemoveAll(n => string.Equals(n, row.InternalName, StringComparison.OrdinalIgnoreCase));
                }

                this.config.Save();
                this.sampler.ApplyConfig();
            }
        }

        ImGui.EndTable();
    }

    private static void Ms(double value, double limit)
    {
        ImGui.TableNextColumn();
        var text = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (value > limit)
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
