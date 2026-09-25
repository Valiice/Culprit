using System.Collections;
using System.Reflection;
using Culprit.Core.Attribution;
using Culprit.Probes;
using Dalamud.Plugin;

namespace Culprit;

public sealed record PluginInfo(string InternalName, string DisplayName, bool IsDev);

/// <summary>
/// The current plugin set from the public API, plus a type-name index built by reflection over
/// each loaded plugin's assembly so update-handler keys can be mapped back to a plugin. The
/// index is incremental: the constructor kicks off a full build on a background thread (so the
/// game does not stall on startup), and once that lands, Rebuild() only reflects over plugins
/// that were not indexed yet, removing entries for plugins that unloaded. Dalamud can raise
/// ActivePluginsChanged on a thread-pool thread, so the handler only sets a dirty flag; the
/// sampler rebuilds on the framework thread via Rebuild() and ConsumeDirty().
/// </summary>
internal sealed class Roster : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly DalamudInternals internals;
    private readonly Dictionary<string, PluginInfo> plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> byDisplayName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> typeNamesByPlugin = new(StringComparer.Ordinal);
    private volatile Dictionary<string, List<string>>? pendingInitial;
    private volatile bool dirty;

    public Roster(IDalamudPluginInterface pluginInterface, DalamudInternals internals)
    {
        this.pluginInterface = pluginInterface;
        this.internals = internals;
        this.RebuildPluginList();
        pluginInterface.ActivePluginsChanged += this.OnActivePluginsChanged;

        this.Indexing = true;
        Task.Run(() =>
        {
            try
            {
                var initial = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var (name, assembly) in this.LoadedPluginAssemblies())
                {
                    initial[name] = TypeNames(assembly);
                }

                this.pendingInitial = initial;
            }
            catch (Exception e)
            {
                Services.Log.Warning(e, "Culprit could not build the initial type index; update handlers will be unattributed");
                this.AttributionStatus = ProbeStatus.Failed("Attribution", e);
            }
            finally
            {
                this.dirty = true;
            }
        });
    }

    public IReadOnlyDictionary<string, PluginInfo> Plugins => this.plugins;

    public TypeIndex TypeIndex { get; } = new();

    public ProbeStatus AttributionStatus { get; private set; } = ProbeStatus.Ok("Attribution");

    /// <summary>True while the initial background type index build has not yet been adopted.</summary>
    public bool Indexing { get; private set; }

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
        this.RebuildPluginList();

        if (this.pendingInitial is { } initial)
        {
            this.typeNamesByPlugin.Clear();
            foreach (var (name, names) in initial)
            {
                this.typeNamesByPlugin[name] = names;
            }

            this.pendingInitial = null;
            this.Indexing = false;
        }

        try
        {
            var loaded = this.LoadedPluginAssemblies();
            var loadedNames = new HashSet<string>(loaded.Select(p => p.Name), StringComparer.Ordinal);

            foreach (var (name, assembly) in loaded)
            {
                if (!this.typeNamesByPlugin.ContainsKey(name))
                {
                    this.typeNamesByPlugin[name] = TypeNames(assembly);
                }
            }

            foreach (var name in this.typeNamesByPlugin.Keys.Where(n => !loadedNames.Contains(n)).ToList())
            {
                this.typeNamesByPlugin.Remove(name);
            }

            this.TypeIndex.Clear();
            foreach (var (name, names) in this.typeNamesByPlugin)
            {
                foreach (var typeName in names)
                {
                    this.TypeIndex.Add(typeName, name);
                }
            }

            this.AttributionStatus = ProbeStatus.Ok("Attribution");
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit could not build the type index; update handlers will be unattributed");
            this.AttributionStatus = ProbeStatus.Failed("Attribution", e);
        }
    }

    private void RebuildPluginList()
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
    }

    /// <summary>The loaded plugins and their assemblies, read via Dalamud's internal LocalPlugin list.</summary>
    private List<(string Name, Assembly Assembly)> LoadedPluginAssemblies()
    {
        var pluginManager = this.internals.GetService("Dalamud.Plugin.Internal.PluginManager");
        var installed = (IEnumerable)(DalamudInternals.Property(pluginManager.GetType(), "InstalledPlugins").GetValue(pluginManager) ?? Array.Empty<object>());
        var localPlugin = this.internals.GetType("Dalamud.Plugin.Internal.Types.LocalPlugin");
        var isLoaded = DalamudInternals.Property(localPlugin, "IsLoaded");
        var internalName = DalamudInternals.Property(localPlugin, "InternalName");
        var assemblyProperty = DalamudInternals.Property(localPlugin, "Assembly");

        var result = new List<(string, Assembly)>();
        foreach (var plugin in installed)
        {
            if (isLoaded.GetValue(plugin) is not true
                || internalName.GetValue(plugin) is not string name
                || assemblyProperty.GetValue(plugin) is not Assembly assembly)
            {
                continue;
            }

            result.Add((name, assembly));
        }

        return result;
    }

    private static List<string> TypeNames(Assembly assembly)
    {
        var names = new List<string>();
        foreach (var type in SafeTypes(assembly))
        {
            if (type.FullName is { } fullName)
            {
                names.Add(fullName);
            }

            // Generic instantiations render differently in Dalamud's handler keys than in FullName.
            var typeString = type.ToString();
            if (typeString != type.FullName)
            {
                names.Add(typeString);
            }
        }

        return names;
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
