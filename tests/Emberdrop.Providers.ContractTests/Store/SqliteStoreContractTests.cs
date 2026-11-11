using Dapper;
using Emberdrop.Storage;
using Emberdrop.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Emberdrop.Providers.ContractTests.Store;

public sealed class SqliteStoreContractTests : RelationalStoreContract
{
    private readonly List<string> _files = [];

    protected override Task<string> CreateEmptyDatabaseAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), $"emberdrop-test-{Guid.NewGuid():N}.db");
        _files.Add(file);
        return Task.FromResult($"Data Source={file}");
    }

    protected override IStoreMigrator CreateMigrator(string connectionString) =>
        new SqliteStoreMigrator(new SqliteStoreOptions { ConnectionString = connectionString });

    protected override IWorkStore CreateStore(string connectionString) =>
        new SqliteWorkStore(new SqliteStoreOptions { ConnectionString = connectionString });

    protected override async Task<IReadOnlyList<(string Name, bool Nullable)>> DescribeWorkUnitTableAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        var rows = await connection.QueryAsync<(string name, long not_null, long pk)>("SELECT name, \"notnull\" AS not_null, pk FROM pragma_table_info('WorkUnitDefinition') ORDER BY cid");
        // A SQLite primary key column is implicitly not null even when the table definition does not say so.
        return rows.Select(r => (r.name, Nullable: r.not_null == 0 && r.pk == 0)).ToList();
    }

    protected override async Task<int> CountAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM EmberdropVersionInfo");
    }

    public override Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in _files)
        {
            foreach (var path in new[] { file, file + "-wal", file + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        return Task.CompletedTask;
    }
}
