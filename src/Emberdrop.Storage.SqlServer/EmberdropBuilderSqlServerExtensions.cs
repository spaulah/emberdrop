using Emberdrop.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Storage.SqlServer;

/// <summary>Selects SQL Server as Emberdrop's work unit store.</summary>
public static class EmberdropBuilderSqlServerExtensions
{
    /// <summary>Stores work unit definitions in a SQL Server database, which must already exist.</summary>
    /// <param name="builder">The Emberdrop builder.</param>
    /// <param name="connectionString">A SQL Server connection string.</param>
    /// <param name="configure">Optionally adjusts <see cref="SqlServerStoreOptions"/>.</param>
    public static IEmberdropBuilder UseSqlServer(this IEmberdropBuilder builder, string connectionString, Action<SqlServerStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new SqlServerStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IStoreMigrator, SqlServerStoreMigrator>();

        return builder.UseStore<SqlServerWorkStore>(ServiceLifetime.Singleton);
    }
}
