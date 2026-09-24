using System.Collections;
using System.Reflection;
using Culprit.Core.Attribution;
using Culprit.Probes;
using Dalamud.Plugin;

namespace Culprit;

public sealed record PluginInfo(string InternalName, string DisplayName, bool IsDev);

/// <summary>
/// The current plugin set from the public API, plus a type-name index built by reflection over
/// each loaded plugin's assembly so update-handler keys can be mapped back to a plugin.
/// Dalamud can raise ActivePluginsChanged on a thread-pool thread, so the handler only sets a
/// dirty flag; the sampler rebuilds on the framework thread via Rebuild() and ConsumeDirty().
/// </summary>
internal sealed class Roster : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly DalamudInternals internals;
    private readonly Dictionary<string, PluginInfo> plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> byDisplayName = new(StringComparer.Ordinal);
    private volatile bool dirty;

    public Roster(IDalamudPluginInterface pluginInterface, DalamudInternals internals)
    {
        this.pluginInterface = pluginInterface;
        this.internals = internals;
        this.Rebuild();
        pluginInterface.ActivePluginsChanged += this.OnActivePluginsChanged;
    }

    public IReadOnlyDictionary<string, PluginInfo> Plugins => this.plugins;

    public TypeIndex TypeIndex { get; } = new();

    public string? DisplayName(string internalName) =>
        this.plugins.TryGetValue(internalName, out var info) ? info.DisplayName : null;

    public string? InternalNameForDisplay(string displayName) =>
        this.byDisplayName.GetValueOrDefault(displayName);

    public string? InternalNameForAssembly(Assembly assembly) =>
        this.pluginInterface.GetPlugin(assembly)?.InternalName;

    /// <summary>Returns whether the plugin set changed since the last call, clearing the flag.</summary>
    public bool ConsumeDirty()
    {
        var wasDirty = this.dirty;
        this.dirty = false;
        return wasDirty;
    }

    public void Dispose() => this.pluginInterface.ActivePluginsChanged -= this.OnActivePluginsChanged;

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => this.dirty = true;

    public void Rebuild()
    {
        this.plugins.Clear();
        this.byDisplayName.Clear();
        foreach (var plugin in this.pluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded)
            {
                continue;
            }

            this.plugins[plugin.InternalName] = new PluginInfo(plugin.InternalName, plugin.Name, plugin.IsDev);
            this.byDisplayName[plugin.Name] = plugin.InternalName;
        }

        this.RebuildTypeIndex();
    }

    private void RebuildTypeIndex()
    {
        this.TypeIndex.Clear();
        try
        {
            var pluginManager = this.internals.GetService("Dalamud.Plugin.Internal.PluginManager");
            var installed = (IEnumerable)(DalamudInternals.Property(pluginManager.GetType(), "InstalledPlugins").GetValue(pluginManager) ?? Array.Empty<object>());
            var localPlugin = this.internals.GetType("Dalamud.Plugin.Internal.Types.LocalPlugin");
            var isLoaded = DalamudInternals.Property(localPlugin, "IsLoaded");
            var internalName = DalamudInternals.Property(localPlugin, "InternalName");
            var assemblyProperty = DalamudInternals.Property(localPlugin, "Assembly");

            foreach (var plugin in installed)
            {
                if (isLoaded.GetValue(plugin) is not true
                    || internalName.GetValue(plugin) is not string name
                    || assemblyProperty.GetValue(plugin) is not Assembly assembly)
                {
                    continue;
                }

                foreach (var type in SafeTypes(assembly))
                {
                    if (type.FullName is { } fullName)
                    {
                        this.TypeIndex.Add(fullName, name);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit could not build the type index; update handlers will be unattributed");
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t is not null).Select(t => t!).ToList();
        }
        catch
        {
            return [];
        }
    }
}
