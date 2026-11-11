using FluentMigrator.Runner.VersionTableInfo;

namespace Emberdrop.Storage.Relational;

/// <summary>
/// Places Emberdrop's migration history in its own table, so it can never collide with a host
/// application's own FluentMigrator history. Providers derive from this and add <c>[VersionTableMetaData]</c>.
/// </summary>
public abstract class EmberdropVersionTableMetaData : IVersionTableMetaData
{
    /// <inheritdoc />
    public object? ApplicationContext { get; set; }

    /// <inheritdoc />
    public bool OwnsSchema => true;

    /// <inheritdoc />
    public string SchemaName => string.Empty;

    /// <inheritdoc />
    public string TableName => "EmberdropVersionInfo";

    /// <inheritdoc />
    public string ColumnName => "Version";

    /// <inheritdoc />
    public string DescriptionColumnName => "Description";

    /// <inheritdoc />
    public string UniqueIndexName => "UC_EmberdropVersion";

    /// <inheritdoc />
    public string AppliedOnColumnName => "AppliedOn";

    /// <inheritdoc />
    public bool CreateWithPrimaryKey => false;
}
