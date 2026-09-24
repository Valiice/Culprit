using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Culprit.Windows;

internal sealed class MainWindow : Window
{
    private readonly LiveTab liveTab;
    private readonly IncidentsTab incidentsTab;
    private readonly SettingsTab settingsTab;

    public MainWindow(Configuration config, Sampler sampler, Roster roster)
        : base("Culprit###CulpritMain")
    {
        this.liveTab = new LiveTab(config, sampler, roster);
        this.incidentsTab = new IncidentsTab(sampler, roster);
        this.settingsTab = new SettingsTab(config, sampler, roster);
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 400),
        };
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##culpritTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Live"))
        {
            this.liveTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Incidents"))
        {
            this.incidentsTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            this.settingsTab.Draw();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
