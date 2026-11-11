using System.IO.Compression;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Microsoft.Extensions.Logging.Abstractions;

namespace Emberdrop.Plugins.Tests.Support;

/// <summary>Builds <c>.emberdrop</c> files for installation tests: from a published fixture, optionally changed, or deliberately hostile.</summary>
internal static class Packages
{
    /// <summary>Zips a copy of a fixture (after <paramref name="change"/> has edited the copy) into <c>&lt;temp&gt;/built/&lt;name&gt;.emberdrop</c>.</summary>
    public static string FromFixture(TempDirectory temp, string fixture, string name, Action<string>? change = null)
    {
        var copy = temp.CopyFixture(fixture, "pkg-" + name);
        change?.Invoke(copy);

        var output = Path.Combine(temp.Path, "built", name + ".emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        ZipFile.CreateFromDirectory(copy, output);
        return output;
    }

    public static Action<string> SetVersion(string version) => directory =>
    {
        var manifest = Path.Combine(directory, "emberdrop.json");
        var text = File.ReadAllText(manifest);
        var start = text.IndexOf("\"version\":", StringComparison.Ordinal);
        var open = text.IndexOf('"', start + 10);
        var close = text.IndexOf('"', open + 1);
        File.WriteAllText(manifest, text[..(open + 1)] + version + text[close..]);
    };

    public static Action<string> ReplaceInManifest(string find, string replace) => directory =>
    {
        var manifest = Path.Combine(directory, "emberdrop.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(find, replace, StringComparison.Ordinal));
    };

    /// <summary>A package with the given entries (name, content) and nothing else, built by hand so that names a normal zip tool would refuse can be used.</summary>
    public static string Raw(TempDirectory temp, string name, params (string EntryName, byte[] Content)[] entries)
    {
        var output = Path.Combine(temp.Path, "built", name + ".emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = File.Create(output);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
            using var entryStream = entry.Open();
            entryStream.Write(content);
        }

        return output;
    }

    /// <summary>Moves a finished package into a drop folder in one step, so a watcher never sees it half written.</summary>
    public static string Drop(string package, string folder)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(package));
        File.Move(package, target, overwrite: true);
        return target;
    }
}

internal static class TestInstallers
{
    public static (PluginInstaller Installer, PluginHost Host, PluginHostOptions Options) Create(TempDirectory temp, Action<PluginHostOptions>? configure = null)
    {
        var (host, options) = TestPlugins.CreateHost(temp, o =>
        {
            o.StableFileCheckInterval = TimeSpan.FromMilliseconds(40);
            o.StableFileChecks = 2;
            o.StableFileTimeout = TimeSpan.FromSeconds(15);
            o.DrainGracePeriod = TimeSpan.FromSeconds(10);
            configure?.Invoke(o);
        });
        var installer = new PluginInstaller(
            host,
            new PluginQuarantine(options, NullLogger<PluginQuarantine>.Instance),
            new PluginArchive(options, NullLogger<PluginArchive>.Instance),
            options,
            NullLogger<PluginInstaller>.Instance);
        installer.PrepareAsync().GetAwaiter().GetResult();
        return (installer, host, options);
    }
}
