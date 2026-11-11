using Emberdrop.Storage;
using FluentAssertions;

namespace Emberdrop.Providers.ContractTests.Store;

/// <summary>
/// Extra behavior every relational storage provider must show on top of <see cref="WorkStoreContract"/>:
/// the same schema and safe repeated migration.
/// </summary>
public abstract class RelationalStoreContract : WorkStoreContract
{
    /// <summary>The columns of the work unit table, in order, with their nullability. Every provider must match this.</summary>
    private static readonly (string Name, bool Nullable)[] ExpectedColumns =
    [
        ("WorkUnitId", false),
        ("Identifier", false),
        ("Name", false),
        ("Type", false),
        ("Enabled", false),
        ("Schedule", false),
        ("Input", true),
        ("CreatedAt", false),
        ("UpdatedAt", false),
        ("PluginId", true),
        ("WorkUnitName", true),
        ("VersionConstraint", true),
        ("InputSchemaVersion", false),
        ("DefaultSchedule", true),
        ("ScheduleOverride", true),
        ("Kind", false),
        ("Description", true),
        ("Capabilities", true),
        ("InputSchema", true),
        ("OutputSchema", true),
    ];

    private string _connectionString = string.Empty;

    /// <summary>Creates a new, empty database and returns its connection string.</summary>
    protected abstract Task<string> CreateEmptyDatabaseAsync();

    protected abstract IStoreMigrator CreateMigrator(string connectionString);

    protected abstract IWorkStore CreateStore(string connectionString);

    protected abstract Task<IReadOnlyList<(string Name, bool Nullable)>> DescribeWorkUnitTableAsync(string connectionString);

    protected abstract Task<int> CountAppliedMigrationsAsync(string connectionString);

    protected override async Task<IWorkStore> CreateStoreAsync()
    {
        _connectionString = await CreateEmptyDatabaseAsync();
        await CreateMigrator(_connectionString).MigrateAsync();
        return CreateStore(_connectionString);
    }

    [SkippableFact]
    public async Task MigrationCreatesTheSharedWorkUnitTableSchema()
    {
        var columns = await DescribeWorkUnitTableAsync(_connectionString);

        columns.Should().Equal(ExpectedColumns);
    }

    [SkippableFact]
    public async Task MigratingAgainChangesNothing()
    {
        var id = await Store.AddAsync(new WorkUnitDefinition { Identifier = "i", Name = "n", Type = "t", Schedule = "* * * * *" });

        await CreateMigrator(_connectionString).MigrateAsync();
        await CreateMigrator(_connectionString).MigrateAsync();

        (await Store.GetAsync(id)).Should().NotBeNull();
        (await CountAppliedMigrationsAsync(_connectionString)).Should().Be(1);
    }
}
