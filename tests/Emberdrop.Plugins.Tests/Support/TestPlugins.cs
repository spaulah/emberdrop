using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Emberdrop.Plugins.Tests.Support;

/// <summary>Helpers for loading the fixture plugins that the test build publishes under <c>fixtures/</c>.</summary>
internal static class TestPlugins
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>Creates a plugin host that treats the fixture contract assembly as shared, with storage in a throwaway directory.</summary>
    public static (PluginHost Host, PluginHostOptions Options) CreateHost(
        TempDirectory storage,
        Action<PluginHostOptions>? configure = null,
        Action<IServiceCollection>? configureHostServices = null,
        IEnumerable<IPluginLifecycleListener>? listeners = null,
        Action<WorkUnitOptions>? configureWorkUnits = null)
    {
        var workUnits = new WorkUnitOptions();
        configureWorkUnits?.Invoke(workUnits);
        var options = new PluginHostOptions
        {
            StorageRoot = Path.Combine(storage.Path, "plugins"),
            UnloadVerificationAttempts = 20,
            UnloadVerificationDelay = TimeSpan.FromMilliseconds(20),
        }.UseWorkUnits(workUnits);
        options.SharedAssemblies.Add("Fixture.Contracts");
        configure?.Invoke(options);

        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        configureHostServices?.Invoke(hostServices);
        var factory = new PluginServiceProviderFactory(hostServices.BuildServiceProvider(), options);

        var host = new PluginHost(new PluginLoader(options, NullLogger<PluginLoader>.Instance), factory, options, listeners ?? [], NullLogger<PluginHost>.Instance);
        return (host, options);
    }

    /// <summary>Retires a version with no waiting and returns the unload verification, for tests that only care whether the code was freed.</summary>
    public static async Task<UnloadResult?> UnloadAsync(this PluginHost host, string pluginId, PluginVersion version) =>
        (await host.RetireAsync(pluginId, version, new DrainOptions { GracePeriod = TimeSpan.Zero, CancellationTimeout = TimeSpan.Zero })).Unload;

    public static IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();
}

/// <summary>A directory that is deleted when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emberdrop-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Copies a published fixture into this directory and returns the copy's path.</summary>
    public string CopyFixture(string fixtureName, string? asName = null)
    {
        var target = System.IO.Path.Combine(Path, asName ?? fixtureName);
        CopyRecursive(TestPlugins.FixturePath(fixtureName), target);
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    private static void CopyRecursive(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyRecursive(directory, System.IO.Path.Combine(target, System.IO.Path.GetFileName(directory)));
        }
    }
}
