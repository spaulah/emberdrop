using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Emberdrop.Plugins;

/// <summary>
/// Decides which assemblies come from the host and checks, before anything is loaded, that a package does not
/// need a newer shared assembly than the host provides.
/// </summary>
/// <remarks>
/// The host version wins for shared assemblies. A plugin that was built against a newer major or minor version
/// than the host has is rejected with an error naming the assembly and both versions. Revision and build
/// differences are accepted.
/// </remarks>
internal sealed class SharedAssemblyPolicy
{
    private static readonly string RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();

    private readonly HashSet<string> _shared;

    public SharedAssemblyPolicy(IEnumerable<string> sharedAssemblies)
    {
        _shared = new HashSet<string>(sharedAssemblies, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the named assembly must always be resolved by the host (shared list or the .NET runtime itself).</summary>
    public bool ResolvesFromHost(string assemblyName) => _shared.Contains(assemblyName) || IsRuntimeAssembly(assemblyName);

    /// <summary>Checks the references of every managed assembly in the package against the host's shared assemblies.</summary>
    public IReadOnlyList<string> FindViolations(string packageDirectory)
    {
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(packageDirectory, "*.dll", SearchOption.AllDirectories))
        {
            (AssemblyIdentity Identity, IReadOnlyList<AssemblyIdentity> References)? metadata;
            try
            {
                metadata = AssemblyMetadata.TryRead(file);
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException)
            {
                problems.Add($"'{Path.GetRelativePath(packageDirectory, file)}' could not be read as an assembly: {ex.Message}");
                continue;
            }

            if (metadata is null)
            {
                continue;
            }

            foreach (var reference in metadata.Value.References.Where(r => _shared.Contains(r.Name)))
            {
                var problem = Check(reference, metadata.Value.Identity.Name);
                if (problem is not null && !problems.Contains(problem))
                {
                    problems.Add(problem);
                }
            }
        }

        return problems;
    }

    private static string? Check(AssemblyIdentity required, string requiredBy)
    {
        var host = HostVersionOf(required.Name);
        if (host is null)
        {
            return $"Assembly '{required.Name}' is shared with the host, but the host does not provide it (required {required.Version} by '{requiredBy}'). " +
                   "Shared assemblies always come from the host.";
        }

        var needsNewer = required.Version.Major > host.Major || (required.Version.Major == host.Major && required.Version.Minor > host.Minor);
        return needsNewer
            ? $"Assembly '{required.Name}' is shared with the host: '{requiredBy}' requires version {required.Version} but the host provides {host}. " +
              "Shared assemblies always come from the host, so upgrade the host or rebuild the plugin against the host's version."
            : null;
    }

    private static Version? HostVersionOf(string assemblyName)
    {
        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null)
        {
            return loaded.GetName().Version;
        }

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName)).GetName().Version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    private static bool IsRuntimeAssembly(string assemblyName) =>
        File.Exists(Path.Combine(RuntimeDirectory, assemblyName + ".dll"));
}
