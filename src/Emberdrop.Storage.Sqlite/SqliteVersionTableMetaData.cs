using Emberdrop.Storage.Relational;
using FluentMigrator.Runner.VersionTableInfo;

namespace Emberdrop.Storage.Sqlite;

/// <summary>Version table for the SQLite provider's migrations.</summary>
[VersionTableMetaData]
public sealed class SqliteVersionTableMetaData : EmberdropVersionTableMetaData;
