using FluentAssertions;
using Emberdrop.Hangfire;
using Emberdrop.InMemory;
using Emberdrop.Scheduling;
using Emberdrop.Server.Extensions;
using Emberdrop.Storage;
using Emberdrop.Storage.Sqlite;
using Emberdrop.Storage.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Server.Tests.Extensions;

public class ProviderSelectionShould
{
    private static ServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmberdropProviders(configuration);
        return services;
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings) => Register(settings).BuildServiceProvider();

    [Fact]
    public void DefaultToInMemorySchedulerAndSqliteSoTheQuickStartNeedsNoSetup()
    {
        using var provider = Build([]);

        provider.GetRequiredService<IWorkScheduler>().Should().BeOfType<InMemoryWorkScheduler>();
        provider.GetRequiredService<IWorkStore>().Should().BeOfType<SqliteWorkStore>();
        provider.GetServices<IStoreMigrator>().Should().ContainSingle().Which.Should().BeOfType<SqliteStoreMigrator>();
    }

    [Fact]
    public async Task ReadTheEmberdropConnectionKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emberdrop-selection-{Guid.NewGuid():N}.db");
        try
        {
            using var provider = Build(new() { ["ConnectionStrings:EmberdropConnection"] = $"Data Source={path}" });

            // The migrator is built from the configured string: migrating creates the file the string points at.
            await provider.GetRequiredService<IStoreMigrator>().MigrateAsync();

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    [Fact]
    public void IgnoreTheOldDefaultConnectionKey()
    {
        // DefaultConnection is no longer read: with only that key set, SQL Server must report the missing connection string.
        var act = () => Build(new() { ["Emberdrop:Store"] = "SqlServer", ["ConnectionStrings:DefaultConnection"] = "Server=x" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*SQL Server connection string*");
    }

    [Fact]
    public void SelectSqlServerWhenConfigured()
    {
        using var provider = Build(new() { ["Emberdrop:Store"] = "SqlServer", ["ConnectionStrings:EmberdropConnection"] = "Server=localhost;Database=Emberdrop" });

        provider.GetRequiredService<IWorkStore>().Should().BeOfType<SqlServerWorkStore>();
        provider.GetServices<IStoreMigrator>().Should().ContainSingle().Which.Should().BeOfType<SqlServerStoreMigrator>();
    }

    [Fact]
    public void SelectTheInMemoryStoreWithoutAMigrator()
    {
        using var provider = Build(new() { ["Emberdrop:Store"] = "InMemory" });

        provider.GetRequiredService<IWorkStore>().Should().BeOfType<InMemoryWorkStore>();
        provider.GetServices<IStoreMigrator>().Should().BeEmpty();
    }

    [Fact]
    public void SelectHangfireWhenItsSqlServerStorageIsConfigured()
    {
        // Inspect the registration instead of resolving: Hangfire's SQL Server storage connects to the database when it is first built.
        var services = Register(new() { ["Emberdrop:Scheduler"] = "Hangfire", ["ConnectionStrings:HangfireConnection"] = "Server=localhost;Database=Hangfire" });

        services.Should().Contain(d => d.ServiceType == typeof(IWorkScheduler) && d.ImplementationType == typeof(HangfireWorkScheduler));
    }

    [Fact]
    public void ExplainWhatHangfireNeedsWhenItsStorageIsMissing()
    {
        var act = () => Build(new() { ["Emberdrop:Scheduler"] = "Hangfire" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Hangfire*HangfireConnection*");
    }

    [Theory]
    [InlineData("Emberdrop:Scheduler", "Quartz", "InMemory, Hangfire")]
    [InlineData("Emberdrop:Store", "Postgres", "Sqlite, SqlServer, InMemory")]
    public void RejectUnknownProvidersAndListTheValidOnes(string setting, string value, string valid)
    {
        var act = () => Build(new() { [setting] = value });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{value}'*{setting}*{valid}*");
    }
}
