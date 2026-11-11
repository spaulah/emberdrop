using System.Data.Common;

namespace Emberdrop.Storage.Relational;

/// <summary>
/// The few places where relational databases differ for <see cref="RelationalWorkStore"/>.
/// Everything else in its SQL is standard and shared.
/// </summary>
public interface ISqlDialect
{
    /// <summary>Creates an unopened connection to the database.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>A statement, run in the same batch as an INSERT, that returns the identity value that INSERT generated.</summary>
    string SelectLastInsertedId { get; }
}
