using System.Collections;
using System.Reflection;
using System.Threading;
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
/// that are not indexed yet or whose assembly changed (a dev plugin reload gets a new
/// ModuleVersionId even though the internal name is unchanged), removing entries for plugins
/// that unloaded. Until the background build lands, Rebuild() only refreshes the plugin list and
/// leaves the type index alone, so every plugin does not look "new" and force a full scan on the
/// framework thread. Dalamud can raise ActivePluginsChanged on a thread-pool thread, so the
/// handler only sets a dirty flag; the sampler rebuilds on the framework thread via Rebuild() and
/// ConsumeDirty().
/// </summary>
internal sealed class Roster : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly DalamudInternals internals;
    private readonly Dictionary<string, PluginInfo> plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> byDisplayName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid Mvid, List<string> Names)> typeNamesByPlugin = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private volatile Dictionary<string, (Guid Mvid, List<string> Names)>? pendingInitial;
    private int dirty;

    public Roster(IDalamudPluginInterface pluginInterface, DalamudInternals internals)
    {
        this.pluginInterface = pluginInterface;
        this.internals = internals;
        this.RebuildPluginList();
        pluginInterface.ActivePluginsChanged += this.OnActivePluginsChanged;

        this.Indexing = true;
        var token = this.cts.Token;
        Task.Run(() =>
        {
            try
            {
                var initial = new Dictionary<string, (Guid, List<string>)>(StringComparer.Ordinal);
                foreach (var (name, assembly) in this.LoadedPluginAssemblies())
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    initial[name] = (assembly.ManifestModule.ModuleVersionId, TypeNames(assembly));
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                this.pendingInitial = initial;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                Services.Log.Warning(e, "Culprit could not build the initial type index; update handlers will be unattributed");
                this.AttributionStatus = ProbeStatus.Failed("Attribution", e);
                this.Indexing = false;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref this.dirty, 1);
                }
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
    public bool ConsumeDirty() => Interlocked.Exchange(ref this.dirty, 0) == 1;

    public void Dispose()
    {
        this.pluginInterface.ActivePluginsChanged -= this.OnActivePluginsChanged;
        this.cts.Cancel();
        this.cts.Dispose();
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => Interlocked.Exchange(ref this.dirty, 1);

    public void Rebuild()
    {
        this.RebuildPluginList();

        if (this.pendingInitial is { } initial)
        {
            this.typeNamesByPlugin.Clear();
            foreach (var (name, entry) in initial)
            {
                this.typeNamesByPlugin[name] = entry;
            }

            this.pendingInitial = null;
            this.Indexing = false;
        }

        if (this.Indexing)
        {
            // The background build has not landed yet: the cache is still empty, so every loaded
            // plugin would look "new" and force a full GetTypes() scan on the framework thread,
            // which is exactly the stall this incremental index exists to avoid. Wait for it; the
            // reconcile below will pick up anything that loaded in the meantime once it lands.
            return;
        }

        try
        {
            var loaded = this.LoadedPluginAssemblies();
            var loadedNames = new HashSet<string>(loaded.Select(p => p.Name), StringComparer.Ordinal);

            foreach (var (name, assembly) in loaded)
            {
                var mvid = assembly.ManifestModule.ModuleVersionId;
                if (!this.typeNamesByPlugin.TryGetValue(name, out var cached) || cached.Mvid != mvid)
                {
                    this.typeNamesByPlugin[name] = (mvid, TypeNames(assembly));
                }
            }

            foreach (var name in this.typeNamesByPlugin.Keys.Where(n => !loadedNames.Contains(n)).ToList())
            {
                this.typeNamesByPlugin.Remove(name);
            }

            this.TypeIndex.Clear();
            foreach (var (name, entry) in this.typeNamesByPlugin)
            {
                foreach (var typeName in entry.Names)
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
