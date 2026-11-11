using Emberdrop.Storage.Relational;

namespace Emberdrop.Storage.SqlServer;

/// <summary>An <see cref="Emberdrop.Storage.IWorkStore"/> backed by SQL Server.</summary>
public sealed class SqlServerWorkStore : RelationalWorkStore
{
    /// <summary>Creates the store.</summary>
    public SqlServerWorkStore(SqlServerStoreOptions options) : base(new SqlServerDialect(), options)
    {
    }
}
