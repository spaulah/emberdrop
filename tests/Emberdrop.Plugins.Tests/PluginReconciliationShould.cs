using Emberdrop.WorkUnits;
using Emberdrop.Plugins;
using Emberdrop.Plugins.Tests.Support;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using FluentAssertions;
using Fixture.Contracts;

namespace Emberdrop.Plugins.Tests;

/// <summary>
/// Stored work units, schedules and plugins kept in step: what gets registered from a manifest, what an operator's choices survive,
/// what pauses when a plugin goes away and resumes when it returns, and what waits.
/// </summary>
public class PluginReconciliationShould
{
    private const string Plugin = "fixture.behaviors";
    private static readonly PluginVersion V1 = PluginVersion.Parse("1.0.0");
    private static readonly PluginVersion V2 = PluginVersion.Parse("2.0.0");
    private static readonly Dictionary<string, string?> Configured = new() { ["Fixture:Setting"] = "set" };

    private static string NewName() => Guid.NewGuid().ToString("N");

    private static ReconcileHarness Harness(TempDirectory temp, Dictionary<string, string?>? configuration = null, Action<WorkUnitOptions>? options = null, params string[] hostWorkUnits) =>
        ReconcileHarness.Create(temp, configuration ?? Configured, options, hostWorkUnits);

    private static Task LoadAsync(ReconcileHarness h, string fixture) => h.Host.LoadAsync(TestPlugins.FixturePath(fixture));

    [Fact]
    public async Task StoreWhatTheManifestAndTheTypesDescribeWithTheWorkUnit()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);

        await LoadAsync(h, "behaviors-v1");

        var echo = await h.WorkUnitAsync(Plugin, "echo");
        (echo.Kind, echo.Description, echo.Capabilities, echo.DefaultSchedule).Should().Be(("work-unit", "Echoes a greeting", """["network:smtp"]""", "30 4 * * *"));
        echo.InputSchema.Should().Contain("\"times\"");
        echo.OutputSchema.Should().Contain("\"greeting\"");
        var count = await h.WorkUnitAsync(Plugin, "count");
        (count.Kind, count.Description, count.Capabilities, count.InputSchema, count.OutputSchema).Should().Be(("work-unit", null, null, null, null));
    }

    [Fact]
    public async Task KeepAnOperatorsTriggerScheduledSeparatelyFromThePluginsDefaultsAcrossAnUpdate()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var echo = await h.WorkUnitAsync(Plugin, "echo");
        var triggerId = await h.Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = echo.WorkUnitId, Kind = "recurring", Definition = "recurring:0 9 * * *" });
        await LoadAsync(h, "behaviors-v2");

        var key = $"{echo.Identifier}:trigger:{triggerId}";
        h.Scheduler.Entries.Should().ContainKey(key);
        h.Scheduler.Entries[key].Cron.Should().Be("0 9 * * *");
        h.Scheduler.Entries[echo.Identifier!].Cron.Should().Be("30 4 * * *", "the plugin's own default stays on the work unit's own entry");
        (await h.Store.ListTriggersAsync(echo.WorkUnitId)).Should().ContainSingle();
    }

    [Fact]
    public async Task RegisterEveryWorkUnitTheManifestDeclaresWithABindingAndTheManifestsSchedule()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);

        await LoadAsync(h, "behaviors-v1");

        var count = await h.WorkUnitAsync(Plugin, "count");
        (count.Type, count.VersionConstraint, count.DefaultSchedule, count.Schedule, count.ScheduleOverride, count.Enabled)
            .Should().Be(("count", "^1.0.0", "0 1 * * *", "0 1 * * *", null, true));
        h.Scheduler.Entries.Should().ContainKey(count.Identifier!);
        h.Scheduler.Entries[count.Identifier!].Should().Match<RecurringWorkSpec>(s => s.Cron == "0 1 * * *" && s.Enabled);

        var gate = await h.WorkUnitAsync(Plugin, "gate");
        (gate.Enabled, gate.DefaultSchedule, gate.Schedule).Should().Be((false, null, WorkUnitScheduleDefaults.NeverSchedule), "no default schedule means the operator decides");

        (await h.WorkUnitAsync(Plugin, "schema2")).InputSchemaVersion.Should().Be(2);
        (await h.Store.ListAsync()).Select(j => j.Identifier).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ReportMissingRequiredConfigurationAtRegistrationAndKeepTheWorkUnitFromRunning()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp, new Dictionary<string, string?>());

        await LoadAsync(h, "behaviors-v1");

        var service = await h.WorkUnitAsync(Plugin, "service");
        service.DefaultSchedule.Should().Be("0 3 * * *");
        service.Enabled.Should().BeFalse("it has a default schedule but cannot run without its setting");
        var status = h.Describe(service);
        status.State.Should().Be(WorkUnitBindingState.MissingConfiguration);
        status.Reason.Should().Contain("Fixture:Setting");
        h.Scheduler.Entries.Should().NotContainKey(service.Identifier!, "a work unit that cannot run is not scheduled");
    }

    [Fact]
    public async Task EnableAWorkUnitWithADefaultScheduleWhenItsConfigurationIsPresent()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);

        await LoadAsync(h, "behaviors-v1");

        var service = await h.WorkUnitAsync(Plugin, "service");
        service.Enabled.Should().BeTrue();
        h.Describe(service).State.Should().Be(WorkUnitBindingState.Ready);
        h.Scheduler.Entries.Should().ContainKey(service.Identifier!);
    }

    [Fact]
    public async Task NeverOverwriteAnOperatorsScheduleOrEnabledFlagWhenAPluginUpdateChangesTheDefault()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var count = await h.WorkUnitAsync(Plugin, "count");
        count.ScheduleOverride = "0 5 * * *";
        count.Schedule = "0 5 * * *";
        count.Enabled = false;
        count.Input = "operator data";
        count.VersionConstraint = "*";
        await h.SetAsync(count);

        await LoadAsync(h, "behaviors-v2");

        var after = await h.WorkUnitAsync(Plugin, "count");
        after.DefaultSchedule.Should().Be("0 2 * * *", "the plugin's default moved with its new version");
        after.ScheduleOverride.Should().Be("0 5 * * *", "the operator's choice wins");
        after.Schedule.Should().Be("0 5 * * *");
        (after.Enabled, after.Input, after.VersionConstraint).Should().Be((false, "operator data", "*"));
        (await h.Store.ListAsync()).Count(j => j.PluginId == Plugin && j.WorkUnitName == "count").Should().Be(1, "updating never duplicates a work unit");
    }

    [Fact]
    public async Task LetANewDefaultScheduleFlowThroughWhenNoOperatorOverrideExists()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");

        await LoadAsync(h, "behaviors-v2");

        var count = await h.WorkUnitAsync(Plugin, "count");
        (count.DefaultSchedule, count.Schedule, count.ScheduleOverride).Should().Be(("0 2 * * *", "0 2 * * *", null));
        h.Scheduler.Entries[count.Identifier!].Cron.Should().Be("0 2 * * *");
    }

    [Fact]
    public async Task PauseSchedulesWhenTheLastVersionLeavesAndResumeThemWhenItReturns()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var count = await h.WorkUnitAsync(Plugin, "count");
        h.Scheduler.Entries.Should().ContainKey(count.Identifier!);

        await h.Host.RetireAsync(Plugin, V1);

        h.Scheduler.Entries.Should().NotContainKey(count.Identifier!, "paused: removed from the scheduler");
        var paused = await h.WorkUnitAsync(Plugin, "count");
        (paused.Schedule, paused.Enabled).Should().Be(("0 1 * * *", true), "paused, not deleted: the stored work unit is untouched");
        h.Describe(paused).Should().Match<WorkUnitBindingStatus>(s => s.State == WorkUnitBindingState.WaitingForPlugin && s.Reason!.Contains("not loaded", StringComparison.Ordinal));

        await LoadAsync(h, "behaviors-v1");

        h.Scheduler.Entries.Should().ContainKey(count.Identifier!, "resumed");
        (await h.Store.ListAsync()).Count(j => j.WorkUnitName == "count").Should().Be(1, "the same stored work unit came back to life");
        h.Describe(await h.WorkUnitAsync(Plugin, "count")).State.Should().Be(WorkUnitBindingState.Ready);
    }

    [Fact]
    public async Task PauseAWorkUnitWhoseConstraintOnlyAllowedTheVersionThatLeftEvenWhenAnotherMajorIsLoaded()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        await LoadAsync(h, "behaviors-v2");
        var count = await h.WorkUnitAsync(Plugin, "count");
        count.VersionConstraint.Should().Be("^1.0.0", "a new work unit follows its plugin within a major version only");

        await h.Host.RetireAsync(Plugin, V1);

        var status = h.Describe(await h.WorkUnitAsync(Plugin, "count"));
        status.State.Should().Be(WorkUnitBindingState.WaitingForPlugin);
        status.Reason.Should().Contain("satisfies ^1.0.0").And.Contain("loaded: 2.0.0");
        h.Scheduler.Entries.Should().NotContainKey(count.Identifier!);

        // Moving to the next major version is the operator's decision.
        count = await h.WorkUnitAsync(Plugin, "count");
        count.VersionConstraint = "*";
        await h.SetAsync(count);
        await h.ScheduleAsync(count);

        h.Describe(count).State.Should().Be(WorkUnitBindingState.Ready);
        h.Scheduler.Entries.Should().ContainKey(count.Identifier!);
    }

    [Fact]
    public async Task RunAStoredWorkUnitAfterItsPluginArrivesLater()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        var id = NewName();
        var stored = new WorkUnitDefinition
        {
            Identifier = "late-count", Name = "late", Type = "count", PluginId = Plugin, WorkUnitName = "count",
            VersionConstraint = "*", Schedule = "0 1 * * *", Enabled = true, Input = id,
        };
        stored.WorkUnitId = await h.Store.AddAsync(stored);
        await h.ScheduleAsync(stored);
        h.Scheduler.Entries.Should().NotContainKey("late-count", "the plugin is not here, so the schedule is paused");
        h.Describe(stored).State.Should().Be(WorkUnitBindingState.WaitingForPlugin);

        await h.FireAsync(stored);

        FixtureControl.CountOf(id).Should().Be(0, "nothing could run");
        h.Waiting.WorkUnitIds.Should().Contain(stored.WorkUnitId, "the missed run is remembered");

        await LoadAsync(h, "behaviors-v1");

        await Eventually(() => FixtureControl.CountOf(id) == 1, "the waiting run to be repeated once the plugin arrived");
        h.Scheduler.Entries.Should().ContainKey("late-count");
        h.Waiting.WorkUnitIds.Should().NotContain(stored.WorkUnitId);
        (await h.Store.ListAsync()).Count(j => j.PluginId == Plugin && j.WorkUnitName == "count").Should().Be(1, "the stored work unit was recognised, not duplicated");
    }

    [Fact]
    public async Task KeepAWorkUnitPinnedToAMissingVersionWaitingAndSayWhy()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var count = await h.WorkUnitAsync(Plugin, "count");
        count.VersionConstraint = "9.9.9";
        count.Input = NewName();
        await h.SetAsync(count);
        await h.ScheduleAsync(count);

        await h.FireAsync(count);

        var status = h.Describe(count);
        status.State.Should().Be(WorkUnitBindingState.WaitingForPlugin);
        status.Reason.Should().Contain("satisfies 9.9.9").And.Contain("loaded: 1.0.0");
        FixtureControl.CountOf(count.Input).Should().Be(0);
        h.Scheduler.Entries.Should().NotContainKey(count.Identifier!);
        h.Waiting.WorkUnitIds.Should().Contain(count.WorkUnitId);
    }

    [Fact]
    public async Task FailInsteadOfWaitingWhenThatIsTheConfiguredPolicy()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp, options: o => o.MissingPlugin = MissingPluginPolicy.Fail);
        await LoadAsync(h, "behaviors-v1");
        var count = await h.WorkUnitAsync(Plugin, "count");
        count.VersionConstraint = "9.9.9";
        await h.SetAsync(count);

        var act = () => h.FireAsync(count);

        (await act.Should().ThrowAsync<PluginNotAvailableException>()).WithMessage("*satisfies 9.9.9*");
        h.Waiting.WorkUnitIds.Should().BeEmpty();
    }

    [Fact]
    public async Task FailFastWithAClearErrorWhenStoredInputDoesNotMatchThePluginsSchema()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var schema2 = await h.WorkUnitAsync(Plugin, "schema2");
        schema2.Input = NewName();
        schema2.InputSchemaVersion = 1; // the plugin expects 2
        await h.SetAsync(schema2);

        var act = () => h.FireAsync(schema2);

        (await act.Should().ThrowAsync<InputSchemaMismatchException>()).WithMessage("*expects input with schema version 2*stored work unit has schema version 1*");
        FixtureControl.IsMarked("schema2-ran:" + schema2.Input).Should().BeFalse("the work unit was never started");
        h.Describe(schema2).State.Should().Be(WorkUnitBindingState.InputSchemaMismatch);
    }

    [Fact]
    public async Task AdoptAWorkStoredBeforeBindingsExistedAndTreatItsHandSetScheduleAsAnOverride()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        var legacy = new WorkUnitDefinition { Identifier = "old-count", Name = "CountWorkUnit", Type = "CountWorkUnit", Schedule = "*/10 * * * *", Enabled = true };
        legacy.WorkUnitId = await h.Store.AddAsync(legacy);

        await LoadAsync(h, "behaviors-v1");

        var adopted = (await h.Store.GetAsync(legacy.WorkUnitId))!;
        (adopted.PluginId, adopted.WorkUnitName, adopted.Type).Should().Be((Plugin, "count", "count"));
        adopted.ScheduleOverride.Should().Be("*/10 * * * *", "the operator had set it by hand");
        adopted.Schedule.Should().Be("*/10 * * * *");
        adopted.DefaultSchedule.Should().Be("0 1 * * *");
        adopted.Identifier.Should().Be("old-count", "the scheduler key does not change");
        (await h.Store.ListAsync()).Count(j => j.WorkUnitName == "count").Should().Be(1);
    }

    [Fact]
    public async Task NeverAdoptAWorkUnitThatIsBuiltIntoTheHost()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp, hostWorkUnits: "CountWorkUnit");
        var builtIn = new WorkUnitDefinition { Identifier = "host-count", Name = "CountWorkUnit", Type = "CountWorkUnit", Schedule = "0 * * * *", Enabled = true };
        builtIn.WorkUnitId = await h.Store.AddAsync(builtIn);

        await LoadAsync(h, "behaviors-v1");

        var untouched = (await h.Store.GetAsync(builtIn.WorkUnitId))!;
        untouched.PluginId.Should().BeNull();
        (await h.Store.ListAsync()).Count(j => j.WorkUnitName == "count").Should().Be(1, "the plugin's work unit got its own stored work unit instead");
    }

    [Fact]
    public async Task KeepAStoredWorkUnitThatANewerVersionNoLongerDeclares()
    {
        using var temp = new TempDirectory();
        await using var h = Harness(temp);
        await LoadAsync(h, "behaviors-v1");
        var directory = temp.CopyFixture("behaviors-v2", "v3-without-fail");
        var manifest = Path.Combine(directory, "emberdrop.json");
        var text = File.ReadAllText(manifest).Replace("\"version\": \"2.0.0\"", "\"version\": \"3.0.0\"");
        File.WriteAllText(manifest, string.Join('\n', text.Split('\n').Where(line => !line.Contains("Fixture.Behaviors.FailWorkUnit", StringComparison.Ordinal))).Replace("\"count\"", "\"count\""));

        await h.Host.LoadAsync(directory);

        (await h.Store.ListAsync()).Should().Contain(j => j.PluginId == Plugin && j.WorkUnitName == "fail", "nothing is deleted when a plugin stops declaring a work unit");
    }

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
}
