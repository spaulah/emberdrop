using System.Reflection;
using System.Runtime.Loader;

namespace Emberdrop.Plugins;

/// <summary>
/// The collectible load context of one plugin version. It resolves the plugin's own dependencies from its
/// <c>deps.json</c>, resolves shared and runtime assemblies from the host, and loads managed code from memory
/// so no file stays open or locked.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly SharedAssemblyPolicy _policy;
    private readonly string _packageDirectory;

    public PluginLoadContext(string name, string firstEntryAssemblyPath, string packageDirectory, SharedAssemblyPolicy policy)
        : base(name, isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(firstEntryAssemblyPath);
        _policy = policy;
        _packageDirectory = packageDirectory;
    }

    /// <summary>Loads an assembly of this plugin from memory: the file is read once and closed, never mapped.</summary>
    public Assembly LoadManagedFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var assembly = new MemoryStream(bytes, writable: false);

        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        if (File.Exists(symbolsPath))
        {
            using var symbols = new MemoryStream(File.ReadAllBytes(symbolsPath), writable: false);
            return LoadFromStream(assembly, symbols);
        }

        return LoadFromStream(assembly);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || _policy.ResolvesFromHost(name))
        {
            // Returning null hands the request to the default context, so the plugin gets the host's copy.
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            // A package without a deps.json: fall back to a file next to the entry assemblies.
            var candidate = Path.Combine(_packageDirectory, name + ".dll");
            path = File.Exists(candidate) ? candidate : null;
        }

        return path is null ? null : LoadManagedFile(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
