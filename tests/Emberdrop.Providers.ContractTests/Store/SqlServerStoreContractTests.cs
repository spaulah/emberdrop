using Dapper;
using Emberdrop.Storage;
using Emberdrop.Storage.SqlServer;
using Microsoft.Data.SqlClient;

namespace Emberdrop.Providers.ContractTests.Store;

/// <summary>Runs the relational store contract against SQL Server. Skipped, with the reason, when no server is available.</summary>
public sealed class SqlServerStoreContractTests : RelationalStoreContract, IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _server;

    public SqlServerStoreContractTests(SqlServerFixture server)
    {
        _server = server;
    }

    protected override async Task<string> CreateEmptyDatabaseAsync()
    {
        Skip.IfNot(_server.Available, _server.UnavailableReason);
        return await _server.CreateDatabaseAsync();
    }

    protected override IStoreMigrator CreateMigrator(string connectionString) =>
        new SqlServerStoreMigrator(new SqlServerStoreOptions { ConnectionString = connectionString });

    protected override IWorkStore CreateStore(string connectionString) =>
        new SqlServerWorkStore(new SqlServerStoreOptions { ConnectionString = connectionString });

    protected override async Task<IReadOnlyList<(string Name, bool Nullable)>> DescribeWorkUnitTableAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string Name, string IsNullable)>(
            "SELECT COLUMN_NAME AS Name, IS_NULLABLE AS IsNullable FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WorkUnitDefinition' ORDER BY ORDINAL_POSITION");
        return rows.Select(r => (r.Name, r.IsNullable == "YES")).ToList();
    }

    protected override async Task<int> CountAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM EmberdropVersionInfo");
    }
}
