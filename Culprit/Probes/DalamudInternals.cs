using System.Reflection;
using Dalamud.Plugin;

namespace Culprit.Probes;

/// <summary>
/// The only place that resolves Dalamud internal types. Every lookup throws a descriptive
/// exception naming the missing type or member, which probes turn into a ProbeStatus reason.
/// Same approach as ECommons' DalamudReflector: Service&lt;T&gt;.Get() closed over an internal type.
/// </summary>
internal sealed class DalamudInternals
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly Assembly dalamud = typeof(IDalamudPluginInterface).Assembly;

    public Type GetType(string fullName) =>
        this.dalamud.GetType(fullName, throwOnError: false)
        ?? throw new TypeLoadException($"type {fullName} not found in Dalamud");

    public object GetService(string typeFullName)
    {
        var serviceType = this.GetType("Dalamud.Service`1").MakeGenericType(this.GetType(typeFullName));
        var get = serviceType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
                  ?? throw new MissingMethodException(serviceType.FullName, "Get");
        return get.Invoke(null, null) ?? throw new InvalidOperationException($"{typeFullName} service is null");
    }

    public static PropertyInfo Property(Type type, string name) =>
        type.GetProperty(name, Any) ?? throw new MissingMemberException(type.FullName, name);

    public static EventInfo Event(Type type, string name) =>
        type.GetEvent(name, Any) ?? throw new MissingMemberException(type.FullName, name);
}
