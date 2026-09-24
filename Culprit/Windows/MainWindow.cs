using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Culprit.Windows;

internal sealed class MainWindow : Window
{
    private readonly Sampler sampler;

    public MainWindow(Configuration config, Sampler sampler, Roster roster)
        : base("Culprit###CulpritMain")
    {
        this.sampler = sampler;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 400),
        };
    }

    public override void Draw()
    {
        foreach (var status in this.sampler.ProbeStatuses)
        {
            ImGui.TextUnformatted($"{status.Name}: {(status.Available ? "ok" : status.Reason)}");
        }
    }
}
