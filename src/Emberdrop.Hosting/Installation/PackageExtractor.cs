using System.IO.Compression;

namespace Emberdrop.Plugins;

/// <summary>
/// Extracts a package into a directory without trusting it. Every entry name is checked before anything is written, and the
/// bytes actually written are counted as they stream out, so an archive whose headers lie about sizes cannot get past the limits.
/// </summary>
public static class PackageExtractor
{
    private const int BufferSize = 81920;
    private const int UnixSymlinkType = 0xA000;
    private const int UnixFileTypeMask = 0xF000;

    /// <summary>Extracts <paramref name="packagePath"/> into <paramref name="destination"/>, which must not exist or must be empty.</summary>
    /// <exception cref="PluginLoadException">The file is not a valid archive, an entry is unsafe, or a limit in <paramref name="options"/> is exceeded. Nothing is left promoted; the caller deletes <paramref name="destination"/>.</exception>
    public static async Task ExtractAsync(string packagePath, string destination, PluginHostOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(options);

        var packageLength = new FileInfo(packagePath).Length;
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(packagePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            throw new PluginLoadException($"The file is not a valid package (it could not be read as a ZIP archive): {ex.Message}");
        }

        using (archive)
        {
            var root = Path.GetFullPath(destination);
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

            var problems = Inspect(archive, rootWithSeparator, options);
            if (problems.Count > 0)
            {
                throw new PluginLoadException(problems);
            }

            Directory.CreateDirectory(root);
            long written = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(rootWithSeparator, entry.FullName));

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                await using var input = entry.Open();
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > options.MaxExtractedBytes)
                    {
                        throw new PluginLoadException($"The package expands to more than {options.MaxExtractedBytes:N0} bytes, which is the limit.");
                    }

                    if (written > Math.Max(packageLength, 1) * (long)options.MaxExpansionRatio)
                    {
                        throw new PluginLoadException(
                            $"The package expands to more than {options.MaxExpansionRatio} times its own size ({packageLength:N0} bytes). That is the signature of a zip bomb, so it was rejected.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static List<string> Inspect(ZipArchive archive, string rootWithSeparator, PluginHostOptions options)
    {
        var problems = new List<string>();
        if (archive.Entries.Count > options.MaxPackageEntries)
        {
            problems.Add($"The package has {archive.Entries.Count:N0} entries; the limit is {options.MaxPackageEntries:N0}.");
            return problems;
        }

        var declared = archive.Entries.Sum(e => e.Length);
        if (declared > options.MaxExtractedBytes)
        {
            problems.Add($"The package declares {declared:N0} bytes when extracted; the limit is {options.MaxExtractedBytes:N0}.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var unsafeReason = UnsafeNameReason(name, rootWithSeparator);
            if (unsafeReason is not null)
            {
                problems.Add($"Entry '{name}' is rejected: {unsafeReason}.");
            }
            else if ((entry.ExternalAttributes >> 16 & UnixFileTypeMask) == UnixSymlinkType)
            {
                problems.Add($"Entry '{name}' is rejected: it is a symbolic link.");
            }
            else if (!seen.Add(name.TrimEnd('/')))
            {
                problems.Add($"Entry '{name}' is rejected: the name appears twice.");
            }
        }

        return problems;
    }

    private static string? UnsafeNameReason(string name, string rootWithSeparator)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "its name is empty";
        }

        if (name.Contains('\\', StringComparison.Ordinal))
        {
            return "its name contains a backslash (path traversal on some platforms)";
        }

        if (name[0] == '/' || (name.Length > 1 && name[1] == ':') || Path.IsPathRooted(name))
        {
            return "its name is an absolute path (path traversal)";
        }

        if (name.Split('/').Any(segment => segment == ".."))
        {
            return "its name goes up out of the package directory (path traversal)";
        }

        var resolved = Path.GetFullPath(Path.Combine(rootWithSeparator, name));
        return resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal) || resolved + Path.DirectorySeparatorChar == rootWithSeparator
            ? null
            : "it would be written outside the package directory (path traversal)";
    }
}
