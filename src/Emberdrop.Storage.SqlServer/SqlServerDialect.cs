using System.Data.Common;
using Emberdrop.Storage.Relational;
using Microsoft.Data.SqlClient;

namespace Emberdrop.Storage.SqlServer;

internal sealed class SqlServerDialect : ISqlDialect
{
    public string SelectLastInsertedId => "SELECT CAST(SCOPE_IDENTITY() AS int);";

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);
}
