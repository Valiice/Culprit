using Culprit.Probes;
using Culprit.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace Culprit;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/culprit";

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("Culprit");
    private readonly Roster? roster;
    private readonly DrawStatsProbe? drawProbe;
    private readonly UpdateStatsProbe? updateProbe;
    private readonly LogProbe? logProbe;
    private readonly Sampler? sampler;
    private readonly MainWindow? mainWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        this.config = Configuration.Load(pluginInterface);

        // Dalamud never calls Dispose on a plugin whose ctor threw, so tear down ourselves.
        try
        {
            var internals = new DalamudInternals();
            this.roster = new Roster(pluginInterface, internals);
            this.drawProbe = new DrawStatsProbe(internals);
            this.updateProbe = new UpdateStatsProbe(internals);
            this.logProbe = new LogProbe(internals);

            var notifier = new Notifier();
            this.sampler = new Sampler(this.config, this.roster, this.drawProbe, this.updateProbe, this.logProbe, notifier);

            this.mainWindow = new MainWindow(this.config, this.sampler, this.roster);
            this.windowSystem.AddWindow(this.mainWindow);

            pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
            pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

            Services.CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Open Culprit. \"/culprit pause\" and \"/culprit resume\" toggle detection, \"/culprit reset\" clears counters.",
            });

            foreach (var status in this.sampler.ProbeStatuses)
            {
                if (status.Available)
                {
                    Services.Log.Info($"[Probe] {status.Name}: available");
                }
                else
                {
                    Services.Log.Warning($"[Probe] {status.Name}: unavailable, {status.Reason}");
                }
            }

            if (this.sampler.ReducedMode)
            {
                notifier.ShowReducedMode(this.sampler.ProbeStatuses.Where(s => !s.Available).Select(s => s.Name));
            }
        }
        catch
        {
            this.Teardown();
            throw;
        }
    }

    public void Dispose() => this.Teardown();

    private void Teardown()
    {
        Guard(() => Services.CommandManager.RemoveHandler(CommandName), "command handler");
        Guard(
            () =>
            {
                Services.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
                Services.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenMain;
                Services.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
                this.windowSystem.RemoveAllWindows();
            },
            "UI wiring");

        // Reverse construction order: the sampler stops ticking before the probes it reads are
        // disposed, and the probes restore Dalamud's stats switches last of all.
        Guard(() => this.sampler?.Dispose(), "sampler");
        Guard(() => this.logProbe?.Dispose(), "log probe");
        Guard(() => this.updateProbe?.Dispose(), "update stats probe");
        Guard(() => this.drawProbe?.Dispose(), "draw stats probe");
        Guard(() => this.roster?.Dispose(), "roster");
    }

    private static void Guard(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Services.Log.Error(e, $"Culprit teardown: {what} failed");
        }
    }

    private void OnCommand(string command, string args)
    {
        if (this.sampler is null)
        {
            return;
        }

        switch (args.Trim().ToLowerInvariant())
        {
            case "pause":
                this.sampler.Paused = true;
                Services.Log.Info("Culprit paused");
                break;
            case "resume":
                this.sampler.Paused = false;
                Services.Log.Info("Culprit resumed");
                break;
            case "reset":
                this.sampler.Reset();
                Services.Log.Info("Culprit counters reset");
                break;
            default:
                this.OpenMain();
                break;
        }
    }

    private void OpenMain()
    {
        if (this.mainWindow is { } window)
        {
            window.IsOpen = true;
        }
    }
}
