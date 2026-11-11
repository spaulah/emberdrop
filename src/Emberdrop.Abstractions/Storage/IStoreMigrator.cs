namespace Emberdrop.Storage;

/// <summary>
/// Brings a storage provider's schema up to date. Storage providers that need a schema register one;
/// the host runs every registered migrator at startup.
/// </summary>
public interface IStoreMigrator
{
    /// <summary>Applies any pending schema changes. Safe to run repeatedly.</summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
