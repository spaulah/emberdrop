using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Fixture.Contracts;

namespace Emberdrop.Plugins.Tests;

/// <summary>
/// Lifecycle, leases and draining, against real compiled plugins whose work units block, ignore cancellation, pin their own
/// load context, or run in the thousands.
/// </summary>
public class PluginDrainingShould
{
    private const string Plugin = "fixture.behaviors";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    private static readonly PluginVersion V1 = PluginVersion.Parse("1.0.0");
    private static readonly PluginVersion V2 = PluginVersion.Parse("2.0.0");

    private static string NewName() => Guid.NewGuid().ToString("N");

    private static async Task<bool> Execute(PluginHost host, string workUnit, string metadata, PluginVersion? pin = null, CancellationToken ct = default)
    {
        var constraint = pin is null ? PluginVersionConstraint.Latest : PluginVersionConstraint.Exactly(pin);
        var result = await host.ExecuteWorkUnitAsync(new WorkUnitBinding(Plugin, workUnit, constraint), new WorkRequest(metadata), 1, ct);
        return result.Executed;
    }

    private static async Task<Task<bool>> StartBlockedWorkUnit(PluginHost host, string workUnit, string gate, PluginVersion? pin = null)
    {
        var run = Execute(host, workUnit, gate, pin);
        await FixtureControl.Started(gate).WaitAsync(TimeSpan.FromSeconds(10));
        return run;
    }

    private static PluginVersionStatus Status(PluginHost host, PluginVersion version) =>
        host.GetVersions().Last(v => v.Id == Plugin && v.Version == version);

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for: " + what);
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Forces collections until the version's context is gone or the time runs out. Used after the cause of a leak was removed.</summary>
    private static async Task WaitUntilContextIsFreed(PluginHost host, PluginVersion version)
    {
        await Eventually(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return Status(host, version).ContextAlive == false;
        }, $"the load context of {version} to be freed");
    }

    // ------------------------------------------------------------ lifecycle and routing

    [Fact]
    public async Task WalkAVersionThroughItsStatesAndRecordThem()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);

        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        Status(host, V1).State.Should().Be(PluginState.Active);

        var result = await host.RetireAsync(Plugin, V1);

        result.Outcome.Should().Be(DrainOutcome.Unloaded);
        result.Unload!.Collected.Should().BeTrue(result.Unload.Diagnostic);
        var status = Status(host, V1);
        status.State.Should().Be(PluginState.Unloaded);
        status.ContextAlive.Should().BeFalse();
        host.GetPlugins().Should().BeEmpty("only active versions can run work");
    }

    [Fact]
    public async Task ReportAnUnknownVersionAsNotFound()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);

        (await host.RetireAsync(Plugin, V1)).Outcome.Should().Be(DrainOutcome.NotFound);
    }

    [Fact]
    public async Task RecordAFailedLoadAsFailedAndLaterAsQuarantinedWhenItsPackageIsMovedAside()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("behaviors-v1", "bad");
        var manifest = Path.Combine(directory, "emberdrop.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("Fixture.Behaviors.GateWorkUnit", "Fixture.Behaviors.Missing"));
        var (host, _) = TestPlugins.CreateHost(temp);

        var failure = (await FluentActions.Awaiting(() => host.LoadAsync(directory)).Should().ThrowAsync<PluginLoadException>()).Which;
        Status(host, V1).State.Should().Be(PluginState.Failed);
        Status(host, V1).Reason.Should().Contain("Missing");

        host.RecordQuarantine("/somewhere/quarantine/bad", failure.Reasons, Plugin, V1);

        Status(host, V1).State.Should().Be(PluginState.Quarantined);
        host.GetVersions().Count(v => v.Id == Plugin).Should().Be(1, "the failed version became quarantined; it was not duplicated");
    }

    [Fact]
    public async Task ListAPackageWithAnUnreadableManifestAsAnUnknownQuarantinedEntry()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);

        host.RecordQuarantine("/q/garbage.emberdrop", ["not a zip"]);

        var status = host.GetVersions().Should().ContainSingle().Which;
        status.Id.Should().Be("(unknown)");
        status.State.Should().Be(PluginState.Quarantined);
        status.Reason.Should().Contain("not a zip");
    }

    [Fact]
    public async Task RunNewWorkOnTheNewestActiveVersionUnlessAWorkUnitIsPinned()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v2"));
        host.GetVersions().Count(v => v.State == PluginState.Active).Should().Be(2, "several versions can be active at once");

        var latest = NewName();
        var pinned = NewName();
        (await Execute(host, "count", latest)).Should().BeTrue();
        (await Execute(host, "count", pinned, pin: V1)).Should().BeTrue();

        FixtureControl.VersionOf(latest).Should().Be("2.0.0");
        FixtureControl.VersionOf(pinned).Should().Be("1.0.0");
    }

    [Fact]
    public async Task GiveTheWorkUnitItsPluginVersionAndLogicalNameInTheContext()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v2"));
        var gate = NewName();

        var run = await StartBlockedWorkUnit(host, "Fixture.Behaviors.GateWorkUnit", gate);
        FixtureControl.OpenGate(gate);
        await run;

        FixtureControl.OutcomeOf(gate + ":context").Should().Be("fixture.behaviors/2.0.0/gate");
    }

    [Fact]
    public async Task ReportThatNothingRanWhenNoActiveVersionProvidesTheWorkUnit()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));

        (await Execute(host, "no-such-work-unit", "x")).Should().BeFalse();
        (await Execute(host, "count", NewName(), pin: V2)).Should().BeFalse("the pinned version is not loaded");
    }

    // ------------------------------------------------------------ hot update

    [Fact]
    public async Task LetInFlightWorkFinishOnTheOldVersionWhileTheNewVersionTakesNewWork()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o => o.DrainGracePeriod = TimeSpan.FromSeconds(30));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var gate = NewName();
        var inFlight = await StartBlockedWorkUnit(host, "gate", gate);
        Status(host, V1).InFlightExecutions.Should().Be(1);

        // v2 arrives and v1 is retired while its work is still running.
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v2"));
        var retiring = host.RetireAsync(Plugin, V1);
        await Eventually(() => Status(host, V1).State == PluginState.Draining, "v1 to start draining");

        // New work goes to v2 and v1 stays loaded.
        var fresh = NewName();
        (await Execute(host, "count", fresh)).Should().BeTrue();
        FixtureControl.VersionOf(fresh).Should().Be("2.0.0");
        await Task.Delay(300);
        retiring.IsCompleted.Should().BeFalse("v1 still has work in flight");
        Status(host, V1).State.Should().Be(PluginState.Draining);
        Status(host, V1).InFlightExecutions.Should().Be(1);
        FixtureControl.OutcomeOf(gate).Should().BeNull("the in-flight work unit was not interrupted");

        // The in-flight work unit finishes on v1, and only then does v1 unload.
        FixtureControl.OpenGate(gate);
        (await inFlight.WaitAsync(Limit)).Should().BeTrue();
        var result = await retiring.WaitAsync(Limit);

        FixtureControl.OutcomeOf(gate).Should().Be("completed on v1");
        result.Outcome.Should().Be(DrainOutcome.Unloaded);
        result.Unload!.Collected.Should().BeTrue(result.Unload.Diagnostic);
        Status(host, V1).State.Should().Be(PluginState.Unloaded);
        Status(host, V2).State.Should().Be(PluginState.Active);
    }

    [Fact]
    public async Task AskInFlightWorkToStopOnceTheGracePeriodIsOverAndUnloadAfterItDoes()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var gate = NewName();
        var inFlight = await StartBlockedWorkUnit(host, "gate", gate);

        var result = await host.RetireAsync(Plugin, V1, new DrainOptions { GracePeriod = TimeSpan.FromMilliseconds(100), CancellationTimeout = TimeSpan.FromSeconds(10) });

        result.Outcome.Should().Be(DrainOutcome.Unloaded);
        result.Unload!.Collected.Should().BeTrue(result.Unload.Diagnostic);
        FixtureControl.OutcomeOf(gate).Should().Be("cancelled on v1", "the work unit saw its CancellationToken");
        await FluentActions.Awaiting(() => inFlight.WaitAsync(Limit)).Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------ stuck

    [Fact]
    public async Task LeaveAVersionDrainingAndFlagItStuckWhenWorkIgnoresCancellation()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var gate = NewName();
        var stubborn = await StartBlockedWorkUnit(host, "stubborn", gate);

        var result = await host.RetireAsync(Plugin, V1, new DrainOptions { GracePeriod = TimeSpan.FromMilliseconds(50), CancellationTimeout = TimeSpan.FromMilliseconds(100) });

        result.Outcome.Should().Be(DrainOutcome.Stuck);
        var status = Status(host, V1);
        status.State.Should().Be(PluginState.Draining, "nothing is forced");
        status.Stuck.Should().BeTrue();
        status.InFlightExecutions.Should().Be(1);
        status.Reason.Should().Contain("did not stop");

        var report = host.GetDiagnosticReport(Plugin, V1)!;
        report.Stuck.Should().BeTrue();
        var execution = report.Executions.Should().ContainSingle().Which;
        execution.ExportName.Should().Be("stubborn");
        execution.CancellationRequested.Should().BeTrue("it was asked and ignored the request");
        execution.Abandoned.Should().BeFalse();
        report.CommonCauses.Should().NotBeEmpty();
        report.Guidance.Should().Contain("dotnet-gcdump");

        // When the work finally ends on its own, the version unloads by itself and is no longer stuck.
        FixtureControl.OpenGate(gate);
        (await stubborn.WaitAsync(Limit)).Should().BeTrue();
        await Eventually(() => Status(host, V1).State == PluginState.Unloaded, "the stuck version to unload once its work ended");
        Status(host, V1).Stuck.Should().BeFalse();
        await WaitUntilContextIsFreed(host, V1);
    }

    [Fact]
    public async Task KeepWaitingWithoutDeclaringStuckWhenToldTo()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var gate = NewName();
        var stubborn = await StartBlockedWorkUnit(host, "stubborn", gate);

        var retiring = host.RetireAsync(Plugin, V1, new DrainOptions
        {
            GracePeriod = TimeSpan.FromMilliseconds(50),
            CancellationTimeout = TimeSpan.FromMilliseconds(50),
            StuckPolicy = StuckPolicy.KeepWaiting,
        });
        await Task.Delay(400);

        retiring.IsCompleted.Should().BeFalse("it keeps waiting");
        Status(host, V1).Stuck.Should().BeFalse();

        FixtureControl.OpenGate(gate);
        await stubborn.WaitAsync(Limit);
        (await retiring.WaitAsync(Limit)).Outcome.Should().Be(DrainOutcome.Unloaded);
    }

    [Fact]
    public async Task FailStuckExecutionsExplicitlyWithoutPretendingTheirCodeStopped()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o =>
        {
            o.UnloadVerificationAttempts = 3;
            o.UnloadVerificationDelay = TimeSpan.FromMilliseconds(10);
        });
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var gate = NewName();
        var stubborn = await StartBlockedWorkUnit(host, "stubborn", gate);

        var result = await host.RetireAsync(Plugin, V1, new DrainOptions
        {
            GracePeriod = TimeSpan.FromMilliseconds(50),
            CancellationTimeout = TimeSpan.FromMilliseconds(50),
            StuckPolicy = StuckPolicy.FailStuckExecutions,
        });

        // The caller is told the execution failed...
        await FluentActions.Awaiting(() => stubborn.WaitAsync(Limit)).Should().ThrowAsync<PluginExecutionAbandonedException>().WithMessage("*abandoned*");
        // ...the version unloaded, but its code is still running, so the runtime could not free it, and the report says why.
        result.Outcome.Should().Be(DrainOutcome.Unloaded);
        result.Unload!.Collected.Should().BeFalse();
        var report = host.GetDiagnosticReport(Plugin, V1)!;
        report.ContextAlive.Should().BeTrue();
        var abandoned = report.Executions.Should().ContainSingle().Which;
        abandoned.Abandoned.Should().BeTrue();
        abandoned.ExportName.Should().Be("stubborn");

        // Once the abandoned code really ends, the context is freed and the report empties.
        FixtureControl.OpenGate(gate);
        await WaitUntilContextIsFreed(host, V1);
        host.GetDiagnosticReport(Plugin, V1)!.Executions.Should().BeEmpty();
    }

    [Fact]
    public async Task HandCallersTheFailureAsTextSoAKeptExceptionCannotPinThePlugin()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));

        // The test keeps the exception alive across the unload, like a scheduler that remembers its last error.
        var kept = (await FluentActions.Awaiting(() => Execute(host, "fail", NewName())).Should().ThrowAsync<PluginExecutionException>()).Which;
        var result = await host.RetireAsync(Plugin, V1);

        kept.Message.Should().Contain("boom from v1").And.Contain("InvalidOperationException").And.Contain("fail");
        kept.Details.Should().Contain("InvalidOperationException").And.Contain("at Fixture.Behaviors.FailWorkUnit");
        kept.InnerException.Should().BeNull("an inner exception would be the plugin's own object");
        result.Unload!.Collected.Should().BeTrue(result.Unload.Diagnostic);
        GC.KeepAlive(kept);
    }

    // ------------------------------------------------------------ pinned contexts

    [Fact]
    public async Task DetectAPluginThatPinsItsContextThroughAStaticEventAndReportIt()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o =>
        {
            o.UnloadVerificationAttempts = 4;
            o.UnloadVerificationDelay = TimeSpan.FromMilliseconds(10);
        });
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        FixtureControl.ClearHostEventSubscribers();

        try
        {
            await Execute(host, "static-event", NewName());
            FixtureControl.HostEventSubscriberCount.Should().Be(1, "the plugin subscribed to a host-side static event");

            var result = await host.RetireAsync(Plugin, V1);

            result.Outcome.Should().Be(DrainOutcome.Unloaded);
            result.Unload!.Collected.Should().BeFalse();
            result.Unload.Diagnostic.Should().Contain("still alive").And.Contain("static event");
            Status(host, V1).ContextAlive.Should().BeTrue();
            Status(host, V1).Reason.Should().Contain("still alive");
            var report = host.GetDiagnosticReport(Plugin, V1)!;
            report.ContextAlive.Should().BeTrue();
            report.CommonCauses.Should().Contain(c => c.Contains("static event", StringComparison.Ordinal));
            report.Guidance.Should().Contain("paths to root");

            // Removing the cause lets the runtime free it: the report was pointing at the right kind of problem.
            FixtureControl.ClearHostEventSubscribers();
            await WaitUntilContextIsFreed(host, V1);
        }
        finally
        {
            FixtureControl.ClearHostEventSubscribers();
        }
    }

    [Fact]
    public async Task DetectAPluginThatLeavesABackgroundThreadRunningAndNameItWhenItWasDeclared()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o =>
        {
            o.UnloadVerificationAttempts = 4;
            o.UnloadVerificationDelay = TimeSpan.FromMilliseconds(10);
        });
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var worker = NewName();

        try
        {
            await Execute(host, "thread", worker);
            host.GetDiagnosticReport(Plugin, V1)!.Activities.Should().ContainSingle(a => a.Kind == "thread" && a.Description.Contains(worker, StringComparison.Ordinal));

            var result = await host.RetireAsync(Plugin, V1);

            result.Unload!.Collected.Should().BeFalse("a running thread references the plugin's code");
            var report = host.GetDiagnosticReport(Plugin, V1)!;
            report.ContextAlive.Should().BeTrue();
            report.Activities.Should().ContainSingle(a => a.Kind == "thread", "the plugin declared it, so the report can name it");
            report.CommonCauses.Should().Contain(c => c.Contains("thread", StringComparison.OrdinalIgnoreCase));

            FixtureControl.StopThread(worker);
            await WaitUntilContextIsFreed(host, V1);
            host.GetDiagnosticReport(Plugin, V1)!.Activities.Should().BeEmpty();
        }
        finally
        {
            FixtureControl.StopThread(worker);
        }
    }

    // ------------------------------------------------------------ churn

    [Fact]
    public async Task RunAThousandWorkUnitsWhileVersionsAreSwappedWithoutLosingOrRepeatingAny()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o => o.DrainGracePeriod = TimeSpan.FromSeconds(30));

        // Six versions of the same plugin. The binaries are identical; only the manifest version differs.
        var versions = Enumerable.Range(1, 6).Select(i => PluginVersion.Parse($"1.0.{i}")).ToList();
        string Directory(PluginVersion v)
        {
            var dir = temp.CopyFixture("behaviors-v1", "v-" + v);
            var manifest = Path.Combine(dir, "emberdrop.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"version\": \"1.0.0\"", $"\"version\": \"{v}\""));
            return dir;
        }

        await host.LoadAsync(Directory(versions[0]));

        const int total = 1000;
        var prefix = NewName();
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var swapper = Task.Run(async () =>
        {
            for (var i = 0; i < versions.Count - 1; i++)
            {
                await Task.Delay(60);
                await host.LoadAsync(Directory(versions[i + 1]));
                var drained = await host.RetireAsync(Plugin, versions[i]);
                drained.Outcome.Should().Be(DrainOutcome.Unloaded);
                drained.Unload!.Collected.Should().BeTrue($"{versions[i]} should be freed: {drained.Unload.Diagnostic}");
            }
        });

        var runs = new List<Task>();
        for (var i = 0; i < total; i++)
        {
            var id = $"{prefix}-{i}";
            runs.Add(Task.Run(async () =>
            {
                try
                {
                    (await Execute(host, "count", id)).Should().BeTrue("some version is always active during a swap");
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }));

            if (i % 8 == 0)
            {
                await Task.Delay(2);
            }
        }

        await Task.WhenAll(runs);
        await swapper;

        failures.Should().BeEmpty();
        var counts = Enumerable.Range(0, total).Select(i => FixtureControl.CountOf($"{prefix}-{i}")).ToList();
        counts.Should().OnlyContain(c => c == 1, "no execution was lost (0) or repeated (2+)");
        var usedVersions = Enumerable.Range(0, total).Select(i => FixtureControl.VersionOf($"{prefix}-{i}")).Distinct().ToList();
        usedVersions.Count.Should().BeGreaterThan(2, "the work really spread across several versions while they were swapped");

        host.GetVersions().Count(v => v.State == PluginState.Active).Should().Be(1);
        host.GetVersions().Where(v => v.State == PluginState.Unloaded).Should().HaveCount(5).And.OnlyContain(v => v.ContextAlive == false);
    }
}
