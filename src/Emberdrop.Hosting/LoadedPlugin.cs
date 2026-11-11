using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Plugins;

/// <summary>
/// One loaded plugin version. It owns the plugin's load context, its types, its object factories and its service
/// provider, and is the only place that holds them, so dropping it (after <see cref="Unload"/>) is what lets the code be freed.
/// </summary>
public sealed class LoadedPlugin
{
    private readonly object _gate = new();
    private readonly PluginLoadContext _context;
    private Type? _startupType;
    private Dictionary<string, ExportEntry>? _exports;
    private ServiceProvider? _services;

    internal LoadedPlugin(PluginManifest manifest, string directory, PluginLoadContext context, Dictionary<string, ExportEntry> exports, Type? startupType)
    {
        Manifest = manifest;
        Directory = directory;
        _context = context;
        _exports = exports;
        _startupType = startupType;
        Descriptor = new PluginDescriptor(
            manifest.Id,
            manifest.Version,
            manifest.EntryAssemblies,
            manifest.Exports.Select(e => new ExportDescriptor(e.Name, e.Kind, exports[e.Name].TypeName, exports[e.Name].AssemblyName, exports[e.Name].Metadata)).ToList());
    }

    /// <summary>The validated manifest.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>The directory the package was loaded from.</summary>
    public string Directory { get; }

    /// <summary>A description of this plugin that holds no plugin types.</summary>
    public PluginDescriptor Descriptor { get; }

    /// <summary>The name of the load context, for logs and diagnostics.</summary>
    public string ContextName => Descriptor.Id + "@" + Descriptor.Version;

    internal bool IsUnloaded
    {
        get
        {
            lock (_gate)
            {
                return _exports is null;
            }
        }
    }

    /// <summary>
    /// Whether this plugin provides the export. <paramref name="key"/> may be the logical export name, the full type name or the simple type name.
    /// </summary>
    public bool HasExport(string key)
    {
        lock (_gate)
        {
            return _exports is not null && Find(_exports, key) is not null;
        }
    }

    /// <summary>Returns the logical export name for a key (an export name, full type name or simple type name), or <see langword="null"/>.</summary>
    internal string? ResolveExportName(string key)
    {
        lock (_gate)
        {
            if (_exports is null)
            {
                return null;
            }

            if (_exports.ContainsKey(key))
            {
                return key;
            }

            return _exports.FirstOrDefault(j => j.Value.TypeName == key || j.Value.SimpleTypeName == key).Key;
        }
    }

    /// <summary>
    /// The plugin's service provider, built on first use. Throws <see cref="PluginLoadException"/> if the plugin's
    /// startup code fails or its registrations cannot be satisfied; only text of the failure is kept.
    /// </summary>
    internal ServiceProvider GetServices(PluginServiceProviderFactory factory)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_exports is null, this);
            return _services ??= BuildServices(factory);
        }
    }

    /// <summary>
    /// Proves the plugin's services can be built, then throws the provider away. A plugin whose startup code throws or whose
    /// registrations cannot be satisfied is rejected at activation instead of at its first run. The real provider is still built lazily.
    /// </summary>
    internal async ValueTask ValidateServicesAsync(PluginServiceProviderFactory factory)
    {
        ServiceProvider provider;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_exports is null, this);
            provider = BuildServices(factory);
        }

        await provider.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="inspect"/> on the type of an export, without creating an instance. Only what the callback returns leaves
    /// the call, so it must be plain data.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The plugin has no such export.</exception>
    internal T Inspect<T>(string key, Func<Type, T> inspect)
    {
        Type type;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_exports is null, this);
            type = (Find(_exports, key) ?? throw new KeyNotFoundException($"Plugin '{ContextName}' has no export '{key}'.")).Type;
        }

        return inspect(type);
    }

    /// <summary>Creates a new instance of an export. Dependencies are resolved from <paramref name="services"/>, normally a per-execution scope of the plugin's provider.</summary>
    /// <exception cref="KeyNotFoundException">The plugin has no such export.</exception>
    public object CreateExport(string key, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ExportEntry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_exports is null, this);
            entry = Find(_exports, key) ?? throw new KeyNotFoundException($"Plugin '{ContextName}' has no export '{key}'.");
        }

        return entry.Factory(services, null);
    }

    /// <summary>Disposes the plugin's service provider, if one was built. Failures in the plugin's disposal code are reported as text.</summary>
    internal async ValueTask<string?> DisposeServicesAsync()
    {
        ServiceProvider? provider;
        lock (_gate)
        {
            provider = _services;
            _services = null;
        }

        if (provider is null)
        {
            return null;
        }

        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Drops every reference this object holds to the plugin's types and asks the runtime to unload the context.</summary>
    /// <returns>A weak reference to the context, for verifying that it is actually collected.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal WeakReference Unload()
    {
        var weak = new WeakReference(_context, trackResurrection: false);
        lock (_gate)
        {
            _exports = null;
            _startupType = null;
            _services = null;
        }

        _context.Unload();
        return weak;
    }

    private ServiceProvider BuildServices(PluginServiceProviderFactory factory)
    {
        try
        {
            return factory.Build(Manifest, _startupType is null ? null : services => RunStartup(_startupType, services));
        }
        catch (PluginLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Text only: the exception may come from plugin code and would pin the load context.
            throw new PluginLoadException(
                $"The services of plugin '{Manifest.Id}' {Manifest.Version} could not be built: {PluginLoadException.Describe(ex)}",
                ex.ToString());
        }
    }

    private static void RunStartup(Type startupType, IServiceCollection services)
    {
        var startup = Activator.CreateInstance(startupType)!;
        var method = startupType.GetMethod("ConfigureServices", [typeof(IServiceCollection)])!;
        method.Invoke(startup, [services]);
    }

    private static ExportEntry? Find(Dictionary<string, ExportEntry> exports, string key)
    {
        if (exports.TryGetValue(key, out var byName))
        {
            return byName;
        }

        return exports.Values.FirstOrDefault(j => j.TypeName == key)
            ?? exports.Values.FirstOrDefault(j => j.SimpleTypeName == key);
    }

    internal sealed class ExportEntry
    {
        public ExportEntry(Type type, object? metadata)
        {
            Type = type;
            Metadata = metadata;
            TypeName = type.FullName ?? type.Name;
            SimpleTypeName = type.Name;
            AssemblyName = type.Assembly.GetName().Name ?? string.Empty;
            // Built once per exported type and held here, so it lives and dies with the plugin's load context.
            Factory = ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);
        }

        public Type Type { get; }

        public object? Metadata { get; }

        public string TypeName { get; }

        public string SimpleTypeName { get; }

        public string AssemblyName { get; }

        public ObjectFactory Factory { get; }
    }
}
