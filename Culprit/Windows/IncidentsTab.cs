using System.Globalization;
using System.Text;
using Culprit.Core.Models;
using Dalamud.Bindings.ImGui;

namespace Culprit.Windows;

internal sealed class IncidentsTab
{
    private readonly Sampler sampler;
    private readonly Roster roster;
    private string filter = string.Empty;

    public IncidentsTab(Sampler sampler, Roster roster)
    {
        this.sampler = sampler;
        this.roster = roster;
    }

    public void Draw()
    {
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "Filter by plugin", ref this.filter, 64);
        ImGui.SameLine();

        var rows = this.sampler.Store.NewestFirst()
            .Where(i => this.filter.Length == 0 || this.Name(i).Contains(this.filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ImGui.Button("Copy"))
        {
            ImGui.SetClipboardText(this.AsText(rows));
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{rows.Count} of {this.sampler.Store.Count}");

        if (!ImGui.BeginTable("##incidents", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthFixed, 160);
        ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Limit", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Detail", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var incident in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(incident.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            if (incident.InternalName is null)
            {
                ImGui.TextColored(Colors.Dim, "no plugin");
            }
            else
            {
                ImGui.TextUnformatted(this.Name(incident));
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(RuleName(incident.Rule));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(incident.Value.ToString("0.0", CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(incident.Limit.ToString("0", CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            if (incident.Notified)
            {
                ImGui.TextUnformatted(incident.Detail);
            }
            else
            {
                ImGui.TextDisabled(incident.Detail);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(incident.Notified ? "Shown as a toast" : "Logged only (cooldown, mute, or unattributed)");
            }
        }

        ImGui.EndTable();
    }

    private static string RuleName(IncidentRule rule) => rule switch
    {
        IncidentRule.Stall => "Stall",
        IncidentRule.Sustained => "Sustained",
        IncidentRule.Errors => "Errors",
        IncidentRule.UnattributedStall => "Unattributed",
        _ => rule.ToString(),
    };

    private string Name(Incident incident) =>
        incident.InternalName is { } n ? this.roster.DisplayName(n) ?? n : "no plugin";

    private string AsText(IEnumerable<Incident> rows)
    {
        var sb = new StringBuilder();
        foreach (var i in rows)
        {
            sb.Append(i.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
              .Append('\t').Append(this.Name(i))
              .Append('\t').Append(RuleName(i.Rule))
              .Append('\t').Append(i.Value.ToString("0.0", CultureInfo.InvariantCulture))
              .Append('\t').Append(i.Limit.ToString("0", CultureInfo.InvariantCulture))
              .Append('\t').Append(i.Detail)
              .Append('\t').Append(i.Notified ? "toast" : "log")
              .AppendLine();
        }

        return sb.ToString();
    }
}
