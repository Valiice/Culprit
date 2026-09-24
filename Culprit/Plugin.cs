using Dalamud.Plugin;

namespace Culprit;

public sealed class Plugin : IDalamudPlugin
{
    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        Services.Log.Info("Culprit scaffold loaded");
    }

    public void Dispose()
    {
    }
}
