using Emberdrop.Hangfire;
using Emberdrop.Hosting;
using Emberdrop.InMemory;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Storage.Sqlite;
using Emberdrop.Storage.SqlServer;
using Hangfire;

namespace Emberdrop.Server.Extensions;

/// <summary>Which scheduler and store the server runs on, read from the <c>Emberdrop</c> configuration section.</summary>
public sealed record ProviderSelection(string Scheduler, string Store)
{
    public const string ConnectionStringName = "EmberdropConnection";
    public const string HangfireConnectionStringName = "HangfireConnection";
    public const string DefaultSqliteConnectionString = "Data Source=emberdrop.db";

    public bool UsesHangfire => Scheduler.Equals("Hangfire", StringComparison.OrdinalIgnoreCase);

    public static ProviderSelection From(IConfiguration configuration) => new(
        configuration["Emberdrop:Scheduler"] ?? "InMemory",
        configuration["Emberdrop:Store"] ?? "Sqlite");
}

public static class EmberdropProviderExtensions
{
    /// <summary>
    /// Registers Emberdrop with the scheduler and store named in configuration.
    /// Defaults to the in-memory scheduler and a SQLite file, which need no setup.
    /// </summary>
    public static IServiceCollection AddEmberdropProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var selection = ProviderSelection.From(configuration);
        var connectionString = configuration.GetConnectionString(ProviderSelection.ConnectionStringName);

        return services.AddEmberdrop(emberdrop =>
        {
            ConfigureStore(emberdrop, selection, connectionString);
            emberdrop.ConfigurePlugins(options =>
            {
                var root = configuration["Plugins:StorageRoot"];
                if (!string.IsNullOrWhiteSpace(root))
                {
                    options.StorageRoot = root;
                }

                foreach (var hash in configuration.GetSection("Plugins:AllowedPackageHashes").Get<string[]>() ?? [])
                {
                    options.AllowedPackageHashes.Add(hash);
                }

                if (int.TryParse(configuration["Plugins:ArchiveRetention"], out var retention))
                {
                    options.ArchiveRetention = retention;
                }
            });
            ConfigureScheduler(emberdrop, selection, configuration, connectionString);
        });
    }

    private static void ConfigureStore(IEmberdropBuilder emberdrop, ProviderSelection selection, string? connectionString)
    {
        switch (selection.Store.ToLowerInvariant())
        {
            case "sqlite":
                emberdrop.UseSqlite(connectionString ?? ProviderSelection.DefaultSqliteConnectionString);
                break;
            case "sqlserver":
                emberdrop.UseSqlServer(RequireSqlServerConnection(connectionString, "Emberdrop:Store=SqlServer"));
                break;
            case "inmemory":
                emberdrop.UseInMemoryStore();
                break;
            default:
                throw UnknownProvider("Emberdrop:Store", selection.Store, "Sqlite, SqlServer, InMemory");
        }
    }

    private static void ConfigureScheduler(IEmberdropBuilder emberdrop, ProviderSelection selection, IConfiguration configuration, string? connectionString)
    {
        switch (selection.Scheduler.ToLowerInvariant())
        {
            case "inmemory":
                emberdrop.UseInMemoryScheduler();
                break;
            case "hangfire":
                // Hangfire keeps its own state in its own storage. This server offers Hangfire's SQL Server storage.
                var hangfireConnection = configuration.GetConnectionString(ProviderSelection.HangfireConnectionStringName)
                    ?? (selection.Store.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? connectionString : null);
                // Resolved now, not inside the callback: Hangfire runs its configuration lazily, which would hide a missing setting until first use.
                var hangfireStorage = RequireSqlServerConnection(
                    hangfireConnection, $"Emberdrop:Scheduler=Hangfire (set ConnectionStrings:{ProviderSelection.HangfireConnectionStringName})");
                emberdrop.UseHangfire(hangfire => hangfire.UseSqlServerStorage(hangfireStorage));
                break;
            default:
                throw UnknownProvider("Emberdrop:Scheduler", selection.Scheduler, "InMemory, Hangfire");
        }
    }

    private static string RequireSqlServerConnection(string? connectionString, string reason) =>
        !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException($"{reason} needs a SQL Server connection string, but none is configured. See docs/database-schema.md.");

    private static InvalidOperationException UnknownProvider(string setting, string value, string valid) =>
        new($"Unknown value '{value}' for {setting}. Valid values: {valid}.");
}
