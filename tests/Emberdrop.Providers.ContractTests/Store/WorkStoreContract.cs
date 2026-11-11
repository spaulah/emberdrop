using Emberdrop.Storage;
using FluentAssertions;

namespace Emberdrop.Providers.ContractTests.Store;

/// <summary>Behavior every <see cref="IWorkStore"/> must show. A provider runs this suite by subclassing it.</summary>
public abstract class WorkStoreContract : IAsyncLifetime
{
    protected IWorkStore Store { get; private set; } = null!;

    /// <summary>Creates a store that is empty and isolated from every other test.</summary>
    protected abstract Task<IWorkStore> CreateStoreAsync();

    public async Task InitializeAsync() => Store = await CreateStoreAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;

    private static WorkUnitDefinition NewWorkUnit(string name = "work unit", string? metadata = "{\"a\":1}") => new()
    {
        Identifier = $"{name}-id",
        Name = name,
        Type = "MyWorkUnitType",
        Schedule = "*/5 * * * *",
        Input = metadata,
        Enabled = true,
    };

    [SkippableFact]
    public async Task AddAssignsDistinctPositiveIdsAndGetReturnsEveryField()
    {
        var first = await Store.AddAsync(NewWorkUnit("a"));
        var second = await Store.AddAsync(NewWorkUnit("b"));

        first.Should().BePositive();
        second.Should().NotBe(first);

        var loaded = await Store.GetAsync(first);
        loaded.Should().NotBeNull();
        loaded!.WorkUnitId.Should().Be(first);
        loaded.Identifier.Should().Be("a-id");
        loaded.Name.Should().Be("a");
        loaded.Type.Should().Be("MyWorkUnitType");
        loaded.Schedule.Should().Be("*/5 * * * *");
        loaded.Input.Should().Be("{\"a\":1}");
        loaded.Enabled.Should().BeTrue();
    }

    [SkippableFact]
    public async Task AddStampsCreationAndUpdateTimes()
    {
        var id = await Store.AddAsync(NewWorkUnit());
        var loaded = (await Store.GetAsync(id))!;

        // Stores write UTC; a store that wrote local time would be off by the machine's UTC offset.
        loaded.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));
        loaded.UpdatedAt.Should().BeOnOrAfter(loaded.CreatedAt);
    }

    [SkippableFact]
    public async Task PluginBindingScheduleAndSchemaFieldsRoundTripAndUpdate()
    {
        var workUnit = NewWorkUnit("bound");
        workUnit.PluginId = "acme.reports";
        workUnit.WorkUnitName = "nightly";
        workUnit.VersionConstraint = "^1.0.0";
        workUnit.InputSchemaVersion = 3;
        workUnit.DefaultSchedule = "0 2 * * *";
        workUnit.ScheduleOverride = "0 4 * * *";

        var id = await Store.AddAsync(workUnit);
        var loaded = (await Store.GetAsync(id))!;

        (loaded.PluginId, loaded.WorkUnitName, loaded.VersionConstraint, loaded.InputSchemaVersion, loaded.DefaultSchedule, loaded.ScheduleOverride)
            .Should().Be(("acme.reports", "nightly", "^1.0.0", 3, "0 2 * * *", "0 4 * * *"));

        loaded.ScheduleOverride = null;
        loaded.VersionConstraint = "1.2.3";
        loaded.InputSchemaVersion = 4;
        await Store.UpdateAsync(loaded);

        var updated = (await Store.GetAsync(id))!;
        (updated.ScheduleOverride, updated.VersionConstraint, updated.InputSchemaVersion).Should().Be((null, "1.2.3", 4));
    }

    [SkippableFact]
    public async Task AWorkUnitWithoutABindingHasNoPluginAndSchemaVersionOne()
    {
        var id = await Store.AddAsync(NewWorkUnit("plain"));

        var loaded = (await Store.GetAsync(id))!;

        (loaded.PluginId, loaded.WorkUnitName, loaded.VersionConstraint, loaded.DefaultSchedule, loaded.ScheduleOverride).Should().Be((null, null, null, null, null));
        loaded.InputSchemaVersion.Should().Be(1);
    }

    [SkippableFact]
    public async Task NullMetadataRoundTrips()
    {
        var id = await Store.AddAsync(NewWorkUnit(metadata: null));

        (await Store.GetAsync(id))!.Input.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetOfAnUnknownIdIsNull()
    {
        (await Store.GetAsync(424242)).Should().BeNull();
    }

    [SkippableFact]
    public async Task ListReturnsEveryStoredWorkUnit()
    {
        (await Store.ListAsync()).Should().BeEmpty();

        await Store.AddAsync(NewWorkUnit("a"));
        await Store.AddAsync(NewWorkUnit("b"));

        (await Store.ListAsync()).Select(j => j.Name).Should().BeEquivalentTo("a", "b");
    }

    [SkippableFact]
    public async Task UpdateOverwritesFieldsAndAdvancesUpdateTimeButKeepsCreationTime()
    {
        var id = await Store.AddAsync(NewWorkUnit("a"));
        var original = (await Store.GetAsync(id))!;
        await Task.Delay(20);

        original.Name = "renamed";
        original.Enabled = false;
        original.Schedule = "0 * * * *";
        original.Input = null;
        await Store.UpdateAsync(original);

        var updated = (await Store.GetAsync(id))!;
        updated.Name.Should().Be("renamed");
        updated.Enabled.Should().BeFalse();
        updated.Schedule.Should().Be("0 * * * *");
        updated.Input.Should().BeNull();
        updated.UpdatedAt.Should().BeAfter(updated.CreatedAt);
        updated.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task UpdateOfAnUnknownWorkUnitChangesNothingAndDoesNotThrow()
    {
        var missing = NewWorkUnit();
        missing.WorkUnitId = 424242;

        var act = () => Store.UpdateAsync(missing);

        await act.Should().NotThrowAsync();
        (await Store.GetAsync(424242)).Should().BeNull();
        (await Store.ListAsync()).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DescriptionKindCapabilitiesAndSchemasRoundTrip()
    {
        var workUnit = NewWorkUnit("described");
        workUnit.Kind = "work-unit";
        workUnit.Description = "Does a thing";
        workUnit.Capabilities = """["network:smtp"]""";
        workUnit.InputSchema = """{"type":"object"}""";
        workUnit.OutputSchema = """{"type":"string"}""";

        var loaded = (await Store.GetAsync(await Store.AddAsync(workUnit)))!;

        (loaded.Kind, loaded.Description, loaded.Capabilities, loaded.InputSchema, loaded.OutputSchema)
            .Should().Be(("work-unit", "Does a thing", """["network:smtp"]""", """{"type":"object"}""", """{"type":"string"}"""));
    }

    [SkippableFact]
    public async Task AWorkUnitWithoutDescriptionsIsATaskWithNoSchemas()
    {
        var loaded = (await Store.GetAsync(await Store.AddAsync(NewWorkUnit("plain"))))!;

        (loaded.Kind, loaded.Description, loaded.Capabilities, loaded.InputSchema, loaded.OutputSchema).Should().Be(("work-unit", null, null, null, null));
    }

    [SkippableFact]
    public async Task OperatorTriggersAreStoredPerWorkUnitAndListedInOrder()
    {
        var first = await Store.AddAsync(NewWorkUnit("a"));
        var second = await Store.AddAsync(NewWorkUnit("b"));
        var t1 = await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = first, Kind = "recurring", Definition = "recurring:0 9 * * *" });
        var t2 = await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = first, Kind = "recurring", Definition = "recurring:0 18 * * *", Enabled = false });
        await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = second, Kind = "recurring", Definition = "recurring:* * * * *" });

        var ofFirst = await Store.ListTriggersAsync(first);

        ofFirst.Select(t => (t.TriggerId, t.Definition, t.Enabled)).Should().Equal((t1, "recurring:0 9 * * *", true), (t2, "recurring:0 18 * * *", false));
        ofFirst.Should().OnlyContain(t => t.CreatedAt != default);
        (await Store.ListTriggersAsync()).Should().HaveCount(3);
    }

    [SkippableFact]
    public async Task DeletingATriggerRemovesOnlyThatTrigger()
    {
        var id = await Store.AddAsync(NewWorkUnit());
        var keep = await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = id, Kind = "recurring", Definition = "recurring:0 9 * * *" });
        var drop = await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = id, Kind = "recurring", Definition = "recurring:0 18 * * *" });

        (await Store.DeleteTriggerAsync(drop)).Should().Be(1);
        (await Store.DeleteTriggerAsync(drop)).Should().Be(0);

        (await Store.ListTriggersAsync(id)).Select(t => t.TriggerId).Should().Equal(keep);
    }

    [SkippableFact]
    public async Task DeletingAWorkUnitDeletesItsTriggers()
    {
        var id = await Store.AddAsync(NewWorkUnit());
        await Store.AddTriggerAsync(new StoredTrigger { WorkUnitId = id, Kind = "recurring", Definition = "recurring:0 9 * * *" });

        await Store.DeleteAsync(id);

        (await Store.ListTriggersAsync(id)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DeleteRemovesTheWorkUnitAndReportsHowManyRowsWentAway()
    {
        var id = await Store.AddAsync(NewWorkUnit());

        (await Store.DeleteAsync(id)).Should().Be(1);
        (await Store.GetAsync(id)).Should().BeNull();
        (await Store.DeleteAsync(id)).Should().Be(0);
    }

    [SkippableFact]
    public async Task ChangingAReturnedObjectDoesNotChangeTheStoredWorkUnitUntilUpdated()
    {
        var id = await Store.AddAsync(NewWorkUnit("a"));
        var copy = (await Store.GetAsync(id))!;

        copy.Name = "local edit";

        (await Store.GetAsync(id))!.Name.Should().Be("a");
    }
}
