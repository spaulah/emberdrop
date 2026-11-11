using System.Data.Common;
using Emberdrop.Storage.Relational;
using Microsoft.Data.Sqlite;

namespace Emberdrop.Storage.Sqlite;

internal sealed class SqliteDialect : ISqlDialect
{
    public string SelectLastInsertedId => "SELECT CAST(last_insert_rowid() AS INTEGER);";

    public DbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);
}
