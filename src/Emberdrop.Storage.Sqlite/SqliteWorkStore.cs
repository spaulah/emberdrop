using Emberdrop.Storage.Relational;

namespace Emberdrop.Storage.Sqlite;

/// <summary>An <see cref="Emberdrop.Storage.IWorkStore"/> backed by a SQLite database.</summary>
public sealed class SqliteWorkStore : RelationalWorkStore
{
    /// <summary>Creates the store.</summary>
    public SqliteWorkStore(SqliteStoreOptions options) : base(new SqliteDialect(), options)
    {
    }
}
