using SharpPack;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

var pluginPath = Path.Combine(
    AppContext.BaseDirectory,
    "plugin",
    "SharpPack.CollectibleAlcPlugin.dll");

var loadContextReference = RunPlugin(pluginPath);

for (var attempt = 0; attempt < 12 && loadContextReference.IsAlive; attempt++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

if (loadContextReference.IsAlive)
{
    throw new InvalidOperationException(
        "The collectible AssemblyLoadContext is still alive.");
}

Console.WriteLine(
    "SharpPack collectible AssemblyLoadContext sample passed.");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference RunPlugin(string pluginPath)
{
    var loadContext = new PluginLoadContext(pluginPath);
    var assembly = loadContext.LoadFromAssemblyPath(pluginPath);
    var entryType = assembly.GetType(
        "SharpPack.CollectibleAlcPlugin.PluginEntry",
        throwOnError: true)!;
    var runMethod = entryType.GetMethod(
        "Run",
        BindingFlags.Public | BindingFlags.Static)!;

    var context = new SharpPackSerializerContext();
    var succeeded = (bool)runMethod.Invoke(null, [context])!;
    if (!succeeded)
    {
        throw new InvalidOperationException(
            "The plugin serialization round-trip failed.");
    }

    var reference = new WeakReference(loadContext);

    context = null!;
    runMethod = null!;
    entryType = null!;
    assembly = null!;
    loadContext.Unload();
    loadContext = null!;

    return reference;
}

sealed class PluginLoadContext : AssemblyLoadContext
{
    readonly AssemblyDependencyResolver resolver;

    public PluginLoadContext(string pluginPath)
        : base("SharpPack collectible sample", isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name ==
            typeof(SharpPackSerializer).Assembly.GetName().Name)
        {
            return typeof(SharpPackSerializer).Assembly;
        }

        var dependencyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return dependencyPath is null
            ? null
            : LoadFromAssemblyPath(dependencyPath);
    }
}
