using System.Diagnostics;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Fixture.Contracts;

namespace Emberdrop.Plugins.Tests;

/// <summary>The install pipeline: complete files only, safe extraction, integrity, atomic promotion, and last-known-good rollback.</summary>
public class PluginInstallerShould
{
    private static readonly PluginVersion V1 = PluginVersion.Parse("1.0.0");

    private static string NewName() => Guid.NewGuid().ToString("N");

    private static string[] Quarantined(PluginHostOptions options) =>
        Directory.Exists(options.QuarantineDirectory)
            ? Directory.GetFileSystemEntries(options.QuarantineDirectory).Where(e => !e.EndsWith(".reason.json", StringComparison.Ordinal)).ToArray()
            : [];

    private static async Task<bool> RunFragileAsync(PluginHost host, string name)
    {
        var result = await host.ExecuteWorkUnitAsync(new WorkUnitBinding("fixture.fragile", "ok", PluginVersionConstraint.Latest), new WorkRequest(name));
        return result.Executed;
    }

    // ------------------------------------------------------------ a normal install

    [Fact]
    public async Task InstallAValidPackageAtomicallyAndLeaveNothingBehind()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        var package = Packages.Drop(Packages.FromFixture(temp, "hello-v1", "hello"), Path.Combine(temp.Path, "drop"));

        var result = await installer.InstallAsync(package);

        result.Outcome.Should().Be(InstallOutcome.Installed);
        (result.PluginId, result.Version).Should().Be(("fixture.hello", V1));
        host.GetPlugins().Should().ContainSingle().Which.Id.Should().Be("fixture.hello");
        var installed = Path.Combine(options.InstalledDirectory, "fixture.hello", "1.0.0");
        File.Exists(Path.Combine(installed, "emberdrop.json")).Should().BeTrue();
        File.Exists(Path.Combine(installed, "package.emberdrop")).Should().BeTrue("the original package is kept for reference");
        File.ReadAllText(Path.Combine(installed, "package.sha256")).Should().HaveLength(64);
        File.Exists(package).Should().BeFalse("the drop folder is emptied");
        Directory.GetDirectories(options.StagingDirectory).Should().BeEmpty("staging is cleaned up");
        Quarantined(options).Should().BeEmpty();
    }

    [Fact]
    public async Task WaitForAPartiallyCopiedFileInsteadOfProcessingIt()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        var bytes = await File.ReadAllBytesAsync(Packages.FromFixture(temp, "hello-v1", "hello"));
        var target = Path.Combine(temp.Path, "drop", "hello.emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        const int chunks = 12;
        var size = bytes.Length / chunks + 1;

        await using var writer = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync(bytes.AsMemory(0, size));
        await writer.FlushAsync();

        // The install starts while only the first part of the file exists; a truncated zip would be rejected if it were read now.
        var install = installer.InstallAsync(target);
        for (var offset = size; offset < bytes.Length; offset += size)
        {
            await Task.Delay(25);
            await writer.WriteAsync(bytes.AsMemory(offset, Math.Min(size, bytes.Length - offset)));
            await writer.FlushAsync();
        }

        await writer.DisposeAsync();
        var result = await install;

        result.Outcome.Should().Be(InstallOutcome.Installed, string.Join("; ", result.Reasons));
        Quarantined(options).Should().BeEmpty("the file was not looked at until it was complete");
        host.GetPlugins().Should().ContainSingle();
    }

    [Fact]
    public async Task WaitForAFileThatIsCompleteButStillOpenForWriting()
    {
        using var temp = new TempDirectory();
        var (installer, host, _) = TestInstallers.Create(temp);
        var bytes = await File.ReadAllBytesAsync(Packages.FromFixture(temp, "hello-v1", "hello"));
        var target = Path.Combine(temp.Path, "drop", "hello.emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // All the bytes are there and the size and time stop changing, but the writer has not closed the file yet.
        var writer = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync(bytes);
        await writer.FlushAsync();
        var watch = Stopwatch.StartNew();
        var install = installer.InstallAsync(target);
        await Task.Delay(500);
        host.GetPlugins().Should().BeEmpty("a file that is still open is not installed");
        await writer.DisposeAsync();
        var result = await install;

        result.Outcome.Should().Be(InstallOutcome.Installed, string.Join("; ", result.Reasons));
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task LeaveAFileThatNeverSettlesWhereItIsInsteadOfQuarantiningIt()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp, o => o.StableFileTimeout = TimeSpan.FromMilliseconds(300));
        var target = Path.Combine(temp.Path, "drop", "growing.emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, new byte[100]);
        using var cancel = new CancellationTokenSource();
        var grower = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                await File.AppendAllTextAsync(target, "more");
                await Task.Delay(20);
            }
        });

        var result = await installer.InstallAsync(target);
        await cancel.CancelAsync();
        await grower;

        result.Outcome.Should().Be(InstallOutcome.Deferred);
        File.Exists(target).Should().BeTrue();
        Quarantined(options).Should().BeEmpty();
        host.GetVersions().Should().BeEmpty();
    }

    [Fact]
    public async Task SkipAFileThatIsAlreadyGone()
    {
        using var temp = new TempDirectory();
        var (installer, _, _) = TestInstallers.Create(temp);

        var result = await installer.InstallAsync(Path.Combine(temp.Path, "drop", "nothing.emberdrop"));

        result.Outcome.Should().Be(InstallOutcome.Skipped);
    }

    // ------------------------------------------------------------ hostile and broken packages

    [Fact]
    public async Task QuarantineACorruptPackageWithAReasonFile()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        var path = Path.Combine(temp.Path, "drop", "corrupt.emberdrop");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "this is not a zip file at all");

        var result = await installer.InstallAsync(path);

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain("not a valid package");
        File.Exists(path).Should().BeFalse();
        var moved = Quarantined(options).Should().ContainSingle().Which;
        File.ReadAllText(moved + ".reason.json").Should().Contain("not a valid package").And.Contain("quarantinedAtUtc");
        host.GetVersions().Should().ContainSingle(v => v.State == PluginState.Quarantined && v.Id == "(unknown)");
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("ABSOLUTE")]
    [InlineData("a\\..\\evil.txt")]
    [InlineData("C:/evil.txt")]
    public async Task RejectAnEntryThatTriesToEscapeTheExtractionDirectory(string entryName)
    {
        // An absolute path is built per test, so a run that writes it (a broken extractor) cannot poison later runs.
        var absolute = Path.Combine(Path.GetTempPath(), "emberdrop-evil-" + Guid.NewGuid().ToString("N") + ".txt");
        entryName = entryName == "ABSOLUTE" ? absolute : entryName;
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        var package = Packages.Drop(
            Packages.Raw(temp, "traversal", ("emberdrop.json", "{}"u8.ToArray()), (entryName, "gotcha"u8.ToArray())),
            Path.Combine(temp.Path, "drop"));

        var result = await installer.InstallAsync(package);

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain(entryName).And.Contain("path traversal");
        new[]
        {
            Path.Combine(options.StagingDirectory, "..", "evil.txt"),
            Path.Combine(options.StagingDirectory, "evil.txt"),
            Path.Combine(temp.Path, "evil.txt"),
            absolute,
        }.Should().OnlyContain(p => !File.Exists(p), "nothing was written outside the extraction directory");
        Directory.GetDirectories(options.StagingDirectory).Should().BeEmpty();
        host.GetPlugins().Should().BeEmpty();
    }

    [Fact]
    public async Task RejectAZipBombByItsExpansionRatioWithoutWritingItOut()
    {
        using var temp = new TempDirectory();
        var (installer, _, options) = TestInstallers.Create(temp, o => o.MaxExpansionRatio = 50);
        // 8 MB of zeros compresses to a few kilobytes: a ratio of well over a thousand.
        var package = Packages.Drop(Packages.Raw(temp, "bomb", ("big.bin", new byte[8 * 1024 * 1024])), Path.Combine(temp.Path, "drop"));
        new FileInfo(package).Length.Should().BeLessThan(100 * 1024, "the test needs a genuinely small file");

        var result = await installer.InstallAsync(package);

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain("zip bomb").And.Contain("50 times");
        Directory.GetDirectories(options.StagingDirectory).Should().BeEmpty("the partial extraction was removed");
    }

    [Fact]
    public async Task RejectAPackageWithTooManyEntriesOrTooManyDeclaredBytes()
    {
        using var temp = new TempDirectory();
        var (installer, _, _) = TestInstallers.Create(temp, o =>
        {
            o.MaxPackageEntries = 3;
            o.MaxExtractedBytes = 1024;
        });
        var tooMany = Packages.Drop(Packages.Raw(temp, "many", ("a", [1]), ("b", [1]), ("c", [1]), ("d", [1])), Path.Combine(temp.Path, "drop"));
        var tooBig = Packages.Drop(Packages.Raw(temp, "big", ("a", new byte[4096])), Path.Combine(temp.Path, "drop"));

        (await installer.InstallAsync(tooMany)).Reasons.Should().ContainSingle().Which.Should().Contain("4 entries").And.Contain("limit is 3");
        (await installer.InstallAsync(tooBig)).Reasons.Should().ContainSingle().Which.Should().Contain("limit is 1,024");
    }

    [Fact]
    public async Task QuarantineAPackageWithoutAManifestOrWithAnInvalidOne()
    {
        using var temp = new TempDirectory();
        var (installer, _, options) = TestInstallers.Create(temp);
        var none = Packages.Drop(Packages.Raw(temp, "nomanifest", ("readme.txt", "hi"u8.ToArray())), Path.Combine(temp.Path, "drop"));
        var bad = Packages.Drop(Packages.Raw(temp, "badmanifest", ("emberdrop.json", """{ "schemaVersion": 1 }"""u8.ToArray())), Path.Combine(temp.Path, "drop"));

        var first = await installer.InstallAsync(none);
        var second = await installer.InstallAsync(bad);

        first.Reasons.Should().ContainSingle().Which.Should().Contain("no emberdrop.json");
        second.Reasons.Count.Should().BeGreaterThan(2, "every problem is listed, not just the first");
        Quarantined(options).Should().HaveCount(2);
    }

    // ------------------------------------------------------------ integrity

    [Fact]
    public async Task RejectAPackageWhoseHashIsNotOnTheAllowlistAndNameTheHash()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp, o => o.AllowedPackageHashes.Add(new string('a', 64)));
        var package = Packages.Drop(Packages.FromFixture(temp, "hello-v1", "hello"), Path.Combine(temp.Path, "drop"));
        var hash = await PackageHasher.Sha256Async(package);

        var result = await installer.InstallAsync(package);

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain(hash).And.Contain("not on the allowlist");
        host.GetPlugins().Should().BeEmpty();
        Quarantined(options).Should().ContainSingle();
    }

    [Fact]
    public async Task InstallAPackageWhoseHashIsOnTheAllowlistRegardlessOfCase()
    {
        using var temp = new TempDirectory();
        var built = Packages.FromFixture(temp, "hello-v1", "hello");
        var hash = (await PackageHasher.Sha256Async(built)).ToUpperInvariant();
        var (installer, host, _) = TestInstallers.Create(temp, o => o.AllowedPackageHashes.Add(hash));

        var result = await installer.InstallAsync(Packages.Drop(built, Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Installed);
        host.GetPlugins().Should().ContainSingle();
    }

    [Fact]
    public async Task DoNoIntegrityCheckWhenTheAllowlistIsEmpty()
    {
        using var temp = new TempDirectory();
        var (installer, _, options) = TestInstallers.Create(temp);
        options.AllowedPackageHashes.Should().BeEmpty("off by default");

        var result = await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v1", "hello"), Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Installed);
    }

    // ------------------------------------------------------------ last known good

    [Fact]
    public async Task KeepThePreviousVersionActiveWhenANewVersionsEntryAssemblyThrowsOnLoad()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        (await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "fragile-v1", "fragile1"), Path.Combine(temp.Path, "drop")))).Outcome.Should().Be(InstallOutcome.Installed);

        var result = await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "fragile-v2", "fragile2"), Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain("entry assembly exploded on load");
        // The old version never stopped being the one in service.
        host.GetPlugins().Should().ContainSingle().Which.Version.Should().Be(V1);
        var name = NewName();
        (await RunFragileAsync(host, name)).Should().BeTrue();
        FixtureControl.IsMarked($"fragile-ran:v1:{name}").Should().BeTrue("the previous version still serves work");
        host.GetVersions().Should().Contain(v => v.Version == PluginVersion.Parse("2.0.0") && v.State == PluginState.Quarantined);
        Directory.Exists(Path.Combine(options.InstalledDirectory, "fixture.fragile", "2.0.0")).Should().BeFalse("the failed version was not left installed");
        Directory.Exists(Path.Combine(options.InstalledDirectory, "fixture.fragile", "1.0.0")).Should().BeTrue();
        File.ReadAllText(Quarantined(options).Single() + ".reason.json").Should().Contain("entry assembly exploded on load");
    }

    [Fact]
    public async Task KeepTheInstalledCopyInServiceWhenAReplacementOfTheSameVersionCannotActivate()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "fragile-v1", "fragile1"), Path.Combine(temp.Path, "drop")));

        // Same id and version 1.0.0, different bytes (the version-2 binaries), and they throw on load.
        var replacement = Packages.FromFixture(temp, "fragile-v2", "fragile-same-version", Packages.SetVersion("1.0.0"));
        var result = await installer.InstallAsync(Packages.Drop(replacement, Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().Contain(r => r.Contains("entry assembly exploded", StringComparison.Ordinal)).And.Contain(r => r.Contains("stays in service", StringComparison.Ordinal));
        host.GetVersions().Count(v => v.State == PluginState.Active).Should().Be(1);
        var name = NewName();
        (await RunFragileAsync(host, name)).Should().BeTrue();
        FixtureControl.IsMarked($"fragile-ran:v1:{name}").Should().BeTrue("the original copy is still the one running");
        Directory.Exists(Path.Combine(options.InstalledDirectory, "fixture.fragile", "1.0.0")).Should().BeTrue("it was not even archived");
        Directory.Exists(Path.Combine(options.ArchiveDirectory, "fixture.fragile")).Should().BeFalse();
    }

    [Fact]
    public async Task KeepThePreviousVersionActiveWhenANewVersionsServicesCannotBeBuilt()
    {
        using var temp = new TempDirectory();
        var (installer, host, _) = TestInstallers.Create(temp);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "behaviors-v1", "b1"), Path.Combine(temp.Path, "drop")));

        var broken = Packages.FromFixture(temp, "behaviors-v2", "b2", Packages.ReplaceInManifest("Fixture.Behaviors.Startup", "Fixture.Behaviors.BrokenStartup"));
        var result = await installer.InstallAsync(Packages.Drop(broken, Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain("could not be built");
        host.GetPlugins().Should().ContainSingle().Which.Version.Should().Be(V1);
    }

    [Fact]
    public async Task KeepThePreviousVersionActiveWhenANewVersionDeclaresAWorkUnitItsAssemblyDoesNotHave()
    {
        using var temp = new TempDirectory();
        var (installer, host, _) = TestInstallers.Create(temp);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "behaviors-v1", "b1"), Path.Combine(temp.Path, "drop")));

        var broken = Packages.FromFixture(temp, "behaviors-v2", "b2", Packages.ReplaceInManifest("Fixture.Behaviors.GateWorkUnit", "Fixture.Behaviors.NoSuchWorkUnit"));
        var result = await installer.InstallAsync(Packages.Drop(broken, Path.Combine(temp.Path, "drop")));

        result.Outcome.Should().Be(InstallOutcome.Quarantined);
        result.Reasons.Should().ContainSingle().Which.Should().Contain("NoSuchWorkUnit");
        host.GetPlugins().Should().ContainSingle().Which.Version.Should().Be(V1);
    }

    [Fact]
    public async Task ReplaceTheSameVersionWithDifferentContentAndArchiveTheOldCopy()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v1", "hello"), Path.Combine(temp.Path, "drop")));

        var changed = Packages.FromFixture(temp, "hello-v1", "hello-changed", dir => File.WriteAllText(Path.Combine(dir, "NOTES.txt"), "a fix"));
        var result = await installer.InstallAsync(Packages.Drop(changed, Path.Combine(temp.Path, "drop")));
        await installer.WhenIdleAsync();

        result.Outcome.Should().Be(InstallOutcome.Installed);
        File.Exists(Path.Combine(options.InstalledDirectory, "fixture.hello", "1.0.0", "NOTES.txt")).Should().BeTrue();
        host.GetVersions().Where(v => v.Id == "fixture.hello").Select(v => v.State).Should().BeEquivalentTo([PluginState.Unloaded, PluginState.Active]);
        Directory.GetDirectories(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Should().ContainSingle();
    }

    [Fact]
    public async Task DoNothingWhenTheSamePackageIsDroppedAgain()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        var built = Packages.FromFixture(temp, "hello-v1", "hello");
        var copy = Path.Combine(temp.Path, "built", "hello-again.emberdrop");
        File.Copy(built, copy);
        await installer.InstallAsync(Packages.Drop(built, Path.Combine(temp.Path, "drop")));

        var second = await installer.InstallAsync(Packages.Drop(copy, Path.Combine(temp.Path, "drop")));

        second.Outcome.Should().Be(InstallOutcome.Unchanged, $"reasons: {string.Join(" | ", second.Reasons)}; versions: {string.Join(", ", host.GetVersions().Select(v => $"{v.Id}@{v.Version}={v.State}"))}");
        File.Exists(Path.Combine(temp.Path, "drop", "hello-again.emberdrop")).Should().BeFalse("the duplicate is removed");
        host.GetVersions().Where(v => v.Id == "fixture.hello").Should().ContainSingle();
        Directory.Exists(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Should().BeFalse();
    }

    [Fact]
    public async Task InstallASecondVersionNextToTheFirstAndRetireTheFirst()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v1", "h1"), Path.Combine(temp.Path, "drop")));

        var result = await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v2", "h2"), Path.Combine(temp.Path, "drop")));
        await installer.WhenIdleAsync();

        result.Outcome.Should().Be(InstallOutcome.Installed);
        host.GetPlugins().Should().ContainSingle().Which.Version.Should().Be(PluginVersion.Parse("2.0.0"));
        host.GetVersions().Single(v => v.Version == V1).State.Should().Be(PluginState.Unloaded);
        Directory.Exists(Path.Combine(options.InstalledDirectory, "fixture.hello", "1.0.0")).Should().BeFalse();
        Directory.GetDirectories(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Should().ContainSingle().Which.Should().Contain("1.0.0_");
    }

    [Fact]
    public async Task KeepSeveralVersionsActiveWhenTheOptionIsOff()
    {
        using var temp = new TempDirectory();
        var (installer, host, _) = TestInstallers.Create(temp, o => o.RetireReplacedVersions = false);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v1", "h1"), Path.Combine(temp.Path, "drop")));

        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v2", "h2"), Path.Combine(temp.Path, "drop")));
        await installer.WhenIdleAsync();

        host.GetPlugins().Select(p => p.Version.ToString()).Should().Equal("2.0.0", "1.0.0");
    }

    // ------------------------------------------------------------ archive retention

    [Fact]
    public async Task KeepOnlyTheConfiguredNumberOfArchivedVersionsNewestFirst()
    {
        using var temp = new TempDirectory();
        var (installer, _, options) = TestInstallers.Create(temp, o => o.ArchiveRetention = 2);

        for (var patch = 1; patch <= 5; patch++)
        {
            var package = Packages.FromFixture(temp, "hello-v1", $"h{patch}", Packages.SetVersion($"1.0.{patch}"));
            (await installer.InstallAsync(Packages.Drop(package, Path.Combine(temp.Path, "drop")))).Outcome.Should().Be(InstallOutcome.Installed);
            await installer.WhenIdleAsync();
            await Task.Delay(5);
        }

        var archived = Directory.GetDirectories(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Select(Path.GetFileName).OrderBy(n => n).ToList();
        archived.Should().HaveCount(2);
        archived[0].Should().StartWith("1.0.3_");
        archived[1].Should().StartWith("1.0.4_");
        Directory.GetDirectories(Path.Combine(options.InstalledDirectory, "fixture.hello")).Should().ContainSingle().Which.Should().EndWith("1.0.5");
    }

    [Fact]
    public async Task KeepNothingInTheArchiveWhenRetentionIsZero()
    {
        using var temp = new TempDirectory();
        var (installer, _, options) = TestInstallers.Create(temp, o => o.ArchiveRetention = 0);
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v1", "h1"), Path.Combine(temp.Path, "drop")));
        await installer.InstallAsync(Packages.Drop(Packages.FromFixture(temp, "hello-v2", "h2"), Path.Combine(temp.Path, "drop")));
        await installer.WhenIdleAsync();

        Directory.GetDirectories(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Should().BeEmpty();
    }

    // ------------------------------------------------------------ startup

    [Fact]
    public async Task LoadWhatIsInstalledAtStartupQuarantineWhatNoLongerLoadsAndClearInterruptedInstalls()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        temp.CopyFixture("hello-v1", Path.Combine("plugins", "installed", "fixture.hello", "1.0.0"));
        temp.CopyFixture("fragile-v2", Path.Combine("plugins", "installed", "fixture.fragile", "2.0.0"));
        var leftover = Path.Combine(options.StagingDirectory, "half-done");
        Directory.CreateDirectory(leftover);
        await File.WriteAllTextAsync(Path.Combine(leftover, "x.dll"), "partial");

        await installer.PrepareAsync();
        var results = await installer.LoadInstalledAsync();

        results.Select(r => r.Outcome).Should().BeEquivalentTo([InstallOutcome.Installed, InstallOutcome.Quarantined]);
        host.GetPlugins().Should().ContainSingle().Which.Id.Should().Be("fixture.hello");
        Directory.Exists(leftover).Should().BeFalse("an interrupted install's leftovers are removed");
        Directory.Exists(Path.Combine(options.InstalledDirectory, "fixture.fragile")).Should().BeFalse("the broken one is out of the way");
        File.ReadAllText(Quarantined(options).Single() + ".reason.json").Should().Contain("entry assembly exploded");
    }

    [Fact]
    public async Task NotLoadAnInstalledPackageThatIsNotOnTheAllowlistAtStartup()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp, o => o.AllowedPackageHashes.Add(new string('b', 64)));
        temp.CopyFixture("hello-v1", Path.Combine("plugins", "installed", "fixture.hello", "1.0.0"));

        var results = await installer.LoadInstalledAsync();

        results.Should().ContainSingle().Which.Outcome.Should().Be(InstallOutcome.Quarantined);
        host.GetPlugins().Should().BeEmpty();
        Quarantined(options).Should().ContainSingle();
    }

    // ------------------------------------------------------------ concurrency and events

    [Fact]
    public async Task InstallSeveralPackagesDroppedAtTheSameTimeWithoutCorruptingEachOther()
    {
        using var temp = new TempDirectory();
        var (installer, host, _) = TestInstallers.Create(temp);
        var packages = new[] { "hello-v1", "behaviors-v1", "fragile-v1", "uses-conflict" }
            .Select(f => Packages.Drop(Packages.FromFixture(temp, f, f), Path.Combine(temp.Path, "drop"))).ToList();

        var results = await Task.WhenAll(packages.Select(p => installer.InstallAsync(p)));

        results.Should().OnlyContain(r => r.Outcome == InstallOutcome.Installed);
        host.GetPlugins().Select(p => p.Id).Should().BeEquivalentTo("fixture.hello", "fixture.behaviors", "fixture.fragile", "fixture.uses-conflict");
    }

    [Fact]
    public async Task TurnManyEventsForOneFileIntoOneUnitOfWork()
    {
        var pending = new PendingPaths();

        var accepted = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => pending.Enqueue("/drop/a.emberdrop"))));

        accepted.Count(a => a).Should().Be(1, "the other 199 events were merged into the one already waiting");
        pending.Enqueue("/drop/b.emberdrop").Should().BeTrue("a different file is separate work");

        var read = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var path in pending.ReadAllAsync(cts.Token))
        {
            read.Add(path);
            if (read.Count == 2)
            {
                break;
            }
        }

        read.Should().Equal("/drop/a.emberdrop", "/drop/b.emberdrop");
        pending.Enqueue("/drop/a.emberdrop").Should().BeTrue("once taken, a path can be queued again, so a change made during processing is not lost");
    }

    [Fact]
    public async Task InstallOnceWhenACopyIntoAWatchedFolderRaisesSeveralEvents()
    {
        using var temp = new TempDirectory();
        var (installer, host, options) = TestInstallers.Create(temp);
        await using var watcher = new DropFolderWatcher(installer, Microsoft.Extensions.Logging.Abstractions.NullLogger<DropFolderWatcher>.Instance);
        var drop = Path.Combine(temp.Path, "watched");
        watcher.Watch(drop);
        watcher.Start();
        var built = Packages.FromFixture(temp, "hello-v1", "hello");

        // A plain copy creates the file, then writes it: more than one event.
        File.Copy(built, Path.Combine(drop, "hello.emberdrop"));

        await EventuallyAsync(() => host.GetPlugins().Count == 1, "the package to be installed");
        await Task.Delay(500);
        await installer.WhenIdleAsync();

        host.GetVersions().Where(v => v.Id == "fixture.hello").Should().ContainSingle("one install, not several");
        File.Exists(Path.Combine(drop, "hello.emberdrop")).Should().BeFalse();
        Quarantined(options).Should().BeEmpty();
        Directory.Exists(Path.Combine(options.ArchiveDirectory, "fixture.hello")).Should().BeFalse("nothing was replaced");
    }

    private static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for: " + what);
            }

            await Task.Delay(25);
        }
    }
}
