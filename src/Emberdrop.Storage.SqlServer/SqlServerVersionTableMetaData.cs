using Emberdrop.Storage.Relational;
using FluentMigrator.Runner.VersionTableInfo;

namespace Emberdrop.Storage.SqlServer;

/// <summary>Version table for the SQL Server provider's migrations.</summary>
[VersionTableMetaData]
public sealed class SqlServerVersionTableMetaData : EmberdropVersionTableMetaData;
