using Emberdrop.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Storage.Sqlite;

/// <summary>Selects SQLite as Emberdrop's work unit store.</summary>
public static class EmberdropBuilderSqliteExtensions
{
    /// <summary>
    /// Stores work unit definitions in a SQLite database. With a file path such as <c>Data Source=emberdrop.db</c>
    /// the file is created on first use; no server or setup is needed.
    /// </summary>
    /// <param name="builder">The Emberdrop builder.</param>
    /// <param name="connectionString">A SQLite connection string.</param>
    /// <param name="configure">Optionally adjusts <see cref="SqliteStoreOptions"/>.</param>
    public static IEmberdropBuilder UseSqlite(this IEmberdropBuilder builder, string connectionString, Action<SqliteStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new SqliteStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IStoreMigrator, SqliteStoreMigrator>();

        return builder.UseStore<SqliteWorkStore>(ServiceLifetime.Singleton);
    }
}
