using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace Culprit.Windows;

internal sealed class SettingsTab
{
    private readonly Configuration config;
    private readonly Sampler sampler;
    private readonly Roster roster;
    private float kofiButtonWidth;

    public SettingsTab(Configuration config, Sampler sampler, Roster roster)
    {
        this.config = config;
        this.sampler = sampler;
        this.roster = roster;
    }

    public void Draw()
    {
        this.DrawKofiButton();

        var changed = false;
        var applied = false;

        ImGui.TextUnformatted("Detection");

        var stallMs = this.config.StallMs;
        if (SliderDouble("Stall (ms in one frame)", ref stallMs, 5, 500, "%.0f"))
        {
            this.config.StallMs = stallMs;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var sustainedMs = this.config.SustainedMs;
        if (SliderDouble("Sustained (ms per frame average)", ref sustainedMs, 0.5, 50, "%.1f"))
        {
            this.config.SustainedMs = sustainedMs;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var sustainedWindow = this.config.SustainedWindowSeconds;
        if (SliderDouble("Sustained window (s)", ref sustainedWindow, 1, 60, "%.0f"))
        {
            this.config.SustainedWindowSeconds = sustainedWindow;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var errorCount = this.config.ErrorCount;
        if (SliderInt("Errors before warning", ref errorCount, 1, 100))
        {
            this.config.ErrorCount = errorCount;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var errorWindow = this.config.ErrorWindowSeconds;
        if (SliderDouble("Error window (s)", ref errorWindow, 5, 600, "%.0f"))
        {
            this.config.ErrorWindowSeconds = errorWindow;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var unattributedMs = this.config.UnattributedMs;
        if (SliderDouble("Unattributed frame (ms)", ref unattributedMs, 20, 1000, "%.0f"))
        {
            this.config.UnattributedMs = unattributedMs;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Alerts");

        var cooldown = this.config.CooldownMinutes;
        if (SliderDouble("Cooldown per plugin and rule (min)", ref cooldown, 0.1, 60, "%.1f"))
        {
            this.config.CooldownMinutes = cooldown;
            applied = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            changed = true;
        }

        var notifyUnattributed = this.config.NotifyUnattributed;
        if (ImGui.Checkbox("Toast unattributed stalls", ref notifyUnattributed))
        {
            this.config.NotifyUnattributed = notifyUnattributed;
            changed = true;
        }

        Tooltip("Frames over the unattributed limit where no plugin crossed the stall limit. The game itself causes most of these, so they are logged but not shown by default.");

        var muted = this.config.Muted;
        if (ImGui.Checkbox("Mute all toasts", ref muted))
        {
            this.config.Muted = muted;
            changed = true;
        }

        Tooltip("Detection and the incident log keep running.");

        var debug = this.config.DebugLog;
        if (ImGui.Checkbox("Write incidents to /xllog (debug level)", ref debug))
        {
            this.config.DebugLog = debug;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Ignored plugins");
        if (this.config.IgnoredPlugins.Count == 0)
        {
            ImGui.TextDisabled("None. Tick Ignore on the Live tab to add one.");
        }

        string? remove = null;
        foreach (var name in this.config.IgnoredPlugins)
        {
            if (ImGuiComponents.IconButton($"##remove{name}", FontAwesomeIcon.Times))
            {
                remove = name;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(this.roster.DisplayName(name) ?? name);
        }

        if (remove is not null)
        {
            this.config.IgnoredPlugins.Remove(remove);
            changed = true;
        }

        if (changed)
        {
            this.config.Save();
            this.sampler.ApplyConfig();
        }
        else if (applied)
        {
            this.sampler.ApplyConfig();
        }
    }

    private static bool SliderDouble(string label, ref double value, double min, double max, string format)
    {
        var f = (float)value;
        if (ImGui.SliderFloat(label, ref f, (float)min, (float)max, format))
        {
            value = f;
            return true;
        }

        return false;
    }

    private static bool SliderInt(string label, ref int value, int min, int max)
    {
        var v = value;
        if (ImGui.SliderInt(label, ref v, min, max))
        {
            value = v;
            return true;
        }

        return false;
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    private void DrawKofiButton()
    {
        var startPos = ImGui.GetCursorPos();
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(startPos.X + available - this.kofiButtonWidth);

        if (ImGuiComponents.IconButtonWithText(
                FontAwesomeIcon.Coffee,
                "Support",
                new Vector4(1.0f, 0.35f, 0.35f, 0.9f),
                new Vector4(1.0f, 0.25f, 0.25f, 1.0f),
                new Vector4(1.0f, 0.35f, 0.35f, 0.75f)))
        {
            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/valiice");
        }

        this.kofiButtonWidth = ImGui.GetItemRectSize().X;
        ImGui.SetCursorPos(startPos);
    }
}
