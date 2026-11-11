using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>
/// Holds every plugin version the host knows about, runs exports on them, and retires them safely.
/// </summary>
/// <remarks>
/// <para>Each version is a state machine (<see cref="PluginState"/>). Several versions of one plugin can be
/// <see cref="PluginState.Active"/> at once; an export asked for by name runs on the newest active version that provides it,
/// unless it is pinned to one.</para>
/// <para>Every execution holds a lease on its version. Retiring a version stops new leases, lets the running ones finish,
/// asks them to stop (through their <see cref="CancellationToken"/>) when they take too long, and only releases the load
/// context once the last lease is gone. Nothing is ever aborted.</para>
/// </remarks>
public sealed partial class PluginHost : IPluginCatalog
{
    private static readonly string[] CommonCauses =
    [
        "A static event or static field that the host or the runtime can reach holds a delegate or object from the plugin (for example a handler added to AppDomain.CurrentDomain.UnhandledException or AssemblyLoadContext.Default.Resolving and never removed).",
        "A thread, timer or thread-pool work item started by the plugin is still running; a running thread's stack references plugin code.",
        "A cache in a shared library is keyed by a plugin type: serializer metadata (System.Text.Json, Newtonsoft.Json), Activator or DI caches, logging category caches.",
        "A thread-local or AsyncLocal value on a long-lived thread references a plugin object.",
        "A finalizable plugin object has not been finalized yet.",
        "A native library loaded by the plugin is still mapped.",
        "An execution was abandoned but its code is still running.",
    ];

    private const string Guidance =
        "Take a heap dump of the host process and look for what roots the plugin's load context: " +
        "`dotnet tool install -g dotnet-gcdump`, then `dotnet-gcdump collect -p <host pid> -o plugin.gcdump`, open the file in Visual Studio or PerfView and search for types from the plugin's assemblies or for the AssemblyLoadContext named after the plugin; " +
        "follow 'paths to root'. See docs/engineering/known-limits.md.";

    private readonly object _gate = new();
    private readonly List<PluginVersionRecord> _records = [];
    private readonly PluginLoader _loader;
    private readonly PluginServiceProviderFactory _services;
    private readonly PluginHostOptions _options;
    private readonly List<IPluginLifecycleListener> _listeners;
    private readonly ILogger<PluginHost> _logger;

    /// <summary>Creates the host.</summary>
    /// <param name="loader">Reads, validates and loads packages.</param>
    /// <param name="services">Builds the service provider each plugin version gets.</param>
    /// <param name="options">The hosting options.</param>
    /// <param name="listeners">Told whenever the active versions of a plugin change.</param>
    /// <param name="logger">The logger.</param>
    public PluginHost(
        PluginLoader loader,
        PluginServiceProviderFactory services,
        PluginHostOptions options,
        IEnumerable<IPluginLifecycleListener> listeners,
        ILogger<PluginHost> logger)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(logger);

        _loader = loader;
        _services = services;
        _options = options;
        _listeners = listeners.ToList();
        _logger = logger;
    }
}
