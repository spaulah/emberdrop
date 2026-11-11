using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Emberdrop.Plugins;

/// <summary>An assembly name and version.</summary>
internal readonly record struct AssemblyIdentity(string Name, Version Version);

/// <summary>
/// Reads assembly identity and references straight from the file's metadata. Nothing is loaded into the
/// process, so no code in the assembly runs and nothing is locked afterwards.
/// </summary>
internal static class AssemblyMetadata
{
    /// <summary>Returns <see langword="null"/> for files that are not managed assemblies (for example native libraries).</summary>
    public static (AssemblyIdentity Identity, IReadOnlyList<AssemblyIdentity> References)? TryRead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        PEReader peReader;
        try
        {
            peReader = new PEReader(stream);
        }
        catch (BadImageFormatException)
        {
            return null;
        }

        using (peReader)
        {
            if (!peReader.HasMetadata)
            {
                return null;
            }

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return null;
            }

            var definition = reader.GetAssemblyDefinition();
            var identity = new AssemblyIdentity(reader.GetString(definition.Name), definition.Version);

            var references = new List<AssemblyIdentity>();
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                references.Add(new AssemblyIdentity(reader.GetString(reference.Name), reference.Version));
            }

            return (identity, references);
        }
    }
}
