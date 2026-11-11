using System.Reflection;
using Microsoft.Extensions.Logging;
using static Emberdrop.Plugins.LoadedPlugin;

namespace Emberdrop.Plugins;

/// <summary>
/// Loads a plugin from an extracted package directory into its own collectible load context.
/// Everything that can be checked without running plugin code is checked first.
/// </summary>
public sealed class PluginLoader
{
    private readonly PluginHostOptions _options;
    private readonly ILogger<PluginLoader> _logger;

    /// <summary>Creates the loader.</summary>
    public PluginLoader(PluginHostOptions options, ILogger<PluginLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <summary>Reads and validates the manifest of the package in <paramref name="directory"/>. Nothing is loaded.</summary>
    /// <exception cref="PluginLoadException">The manifest is missing or invalid; the exception lists every problem found.</exception>
    public static async Task<PluginManifest> ReadManifestAsync(string directory, ManifestFormat? format = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var result = await PluginManifestReader.ReadFromDirectoryAsync(Path.GetFullPath(directory), format, cancellationToken).ConfigureAwait(false);
        return result.Manifest ?? throw new PluginLoadException(result.Errors);
    }

    /// <summary>
    /// Runs every check that does not need the plugin's code: version requirements, entry assemblies, and the
    /// shared-assembly rules (from metadata only).
    /// </summary>
    /// <exception cref="PluginLoadException">The package cannot be used; the exception lists every problem found.</exception>
    public ValidatedPackage Validate(PluginManifest manifest, string directory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var packageDirectory = Path.GetFullPath(directory);
        var policy = new SharedAssemblyPolicy(_options.SharedAssemblies);
        var entryPaths = ValidateBeforeLoading(manifest, packageDirectory, policy);
        return new ValidatedPackage(manifest, packageDirectory, entryPaths, policy);
    }

    /// <summary>Loads a validated package into a new collectible load context and resolves its exports.</summary>
    /// <exception cref="PluginLoadException">The package could not be activated.</exception>
    public async Task<LoadedPlugin> LoadAsync(ValidatedPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        return await Task.Run(() => LoadCore(package), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads, validates and loads the package in <paramref name="directory"/> in one step.</summary>
    /// <exception cref="PluginLoadException">The package is invalid; the exception lists every problem found.</exception>
    public async Task<LoadedPlugin> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(directory, _options.ManifestFormat, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(Validate(manifest, directory), cancellationToken).ConfigureAwait(false);
    }

    private LoadedPlugin LoadCore(ValidatedPackage package)
    {
        var manifest = package.Manifest;
        var context = new PluginLoadContext($"plugin:{manifest.Id}@{manifest.Version}", package.EntryPaths[0], package.Directory, package.Policy);
        try
        {
            var assemblies = package.EntryPaths.Select(context.LoadManagedFile).ToList();

            // Run each entry assembly's module initializer now. Otherwise an assembly that throws when its code first runs
            // would pass activation and fail on an export's first use; failing here keeps the previous version in service.
            foreach (var assembly in assemblies)
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }

            var exports = ResolveExports(manifest, assemblies);
            var startup = ResolveStartup(manifest, assemblies);

            _logger.LogInformation("Loaded plugin {PluginId} {Version} with {ExportCount} export(s) into context {Context}",
                manifest.Id, manifest.Version, exports.Count, context.Name);
            return new LoadedPlugin(manifest, package.Directory, context, exports, startup);
        }
        catch (Exception ex) when (ex is not PluginLoadException)
        {
            context.Unload();
            // Keep only text: the exception object may reference the plugin's types and would pin its load context.
            throw new PluginLoadException(
                $"Plugin '{manifest.Id}' {manifest.Version} could not be loaded: {PluginLoadException.Describe(ex)}",
                ex.ToString());
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static List<string> ValidateBeforeLoading(PluginManifest manifest, string packageDirectory, SharedAssemblyPolicy policy)
    {
        var problems = new List<string>();

        var hostAbstractions = HostAbstractionsVersion();
        if (manifest.MinAbstractionsVersion.Major > hostAbstractions.Major
            || (manifest.MinAbstractionsVersion.Major == hostAbstractions.Major && manifest.MinAbstractionsVersion.Minor > hostAbstractions.Minor))
        {
            problems.Add($"The plugin requires Emberdrop.Abstractions {manifest.MinAbstractionsVersion} or newer but this host provides {hostAbstractions}.");
        }

        var entryPaths = new List<string>();
        foreach (var entry in manifest.EntryAssemblies)
        {
            var full = Path.GetFullPath(Path.Combine(packageDirectory, entry));
            if (!full.StartsWith(packageDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                problems.Add($"Entry assembly '{entry}' resolves outside the package.");
            }
            else if (!File.Exists(full))
            {
                problems.Add($"Entry assembly '{entry}' declared in the manifest is not in the package.");
            }
            else
            {
                entryPaths.Add(full);
            }
        }

        if (problems.Count == 0)
        {
            problems.AddRange(policy.FindViolations(packageDirectory));
        }

        if (problems.Count > 0)
        {
            throw new PluginLoadException(problems);
        }

        return entryPaths;
    }

    private Dictionary<string, ExportEntry> ResolveExports(PluginManifest manifest, List<Assembly> assemblies)
    {
        var noun = _options.ManifestFormat.ExportNoun;
        var problems = new List<string>();
        var exports = new Dictionary<string, ExportEntry>(StringComparer.Ordinal);

        foreach (var export in manifest.Exports)
        {
            var type = assemblies.Select(a => a.GetType(export.Type, throwOnError: false)).FirstOrDefault(t => t is not null);
            if (type is null)
            {
                problems.Add($"{Capitalize(noun)} '{export.Name}' declares type '{export.Type}', which was not found in the entry assemblies.");
                continue;
            }

            if (type.IsAbstract || type.IsInterface)
            {
                problems.Add($"{Capitalize(noun)} '{export.Name}' declares type '{export.Type}', which is not a concrete class.");
                continue;
            }

            var before = problems.Count;
            object? metadata = _options.ExportHandler.Inspect(export, type, problems);
            if (problems.Count == before)
            {
                exports[export.Name] = new ExportEntry(type, metadata);
            }
        }

        return problems.Count > 0 ? throw new PluginLoadException(problems) : exports;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static Type? ResolveStartup(PluginManifest manifest, List<Assembly> assemblies)
    {
        if (manifest.Startup is null)
        {
            return null;
        }

        var type = assemblies.Select(a => a.GetType(manifest.Startup, throwOnError: false)).FirstOrDefault(t => t is not null);
        if (type is null)
        {
            throw new PluginLoadException($"The startup type '{manifest.Startup}' declared in the manifest was not found in the entry assemblies.");
        }

        if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new PluginLoadException($"The startup type '{manifest.Startup}' must be a concrete class with a public parameterless constructor.");
        }

        var method = type.GetMethod("ConfigureServices", [typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)]);
        if (method is null || method.IsStatic)
        {
            throw new PluginLoadException($"The startup type '{manifest.Startup}' must have a public instance method 'void ConfigureServices(IServiceCollection services)'.");
        }

        return type;
    }

    private static Version HostAbstractionsVersion()
    {
        var version = typeof(PluginVersion).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
