using Dapper;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Emberdrop.Providers.ContractTests.Store;

/// <summary>
/// Provides a SQL Server for the store contract tests, without ever failing a machine that has none.
/// Order of preference: the server named by the <c>EMBERDROP_TEST_SQLSERVER</c> environment variable, then a
/// disposable container started with Testcontainers. If neither is possible, <see cref="Available"/> is false and
/// the tests report themselves as skipped with <see cref="UnavailableReason"/>.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "EMBERDROP_TEST_SQLSERVER";

    private readonly List<string> _databases = [];
    private MsSqlContainer? _container;
    private string? _adminConnectionString;

    public bool Available { get; private set; }

    public string UnavailableReason { get; private set; } = "SQL Server was not started.";

    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            _adminConnectionString = existing;
            Available = true;
            return;
        }

        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            UnavailableReason =
                $"SQL Server is not available ({ex.GetType().Name}: {FirstLine(ex.Message)}). " +
                $"Start Docker so Testcontainers can run SQL Server, or set {ConnectionStringVariable} to a server you can create databases on.";
        }
    }

    /// <summary>Creates an empty database with a unique name and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"emberdrop_{Guid.NewGuid():N}";
        await using (var admin = new SqlConnection(_adminConnectionString))
        {
            await admin.ExecuteAsync($"CREATE DATABASE [{name}]");
        }

        _databases.Add(name);
        return new SqlConnectionStringBuilder(_adminConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is not null && _container is null)
        {
            // A shared server outlives the tests, so leave it as we found it.
            SqlConnection.ClearAllPools();
            await using var admin = new SqlConnection(_adminConnectionString);
            foreach (var name in _databases)
            {
                await admin.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();
}
