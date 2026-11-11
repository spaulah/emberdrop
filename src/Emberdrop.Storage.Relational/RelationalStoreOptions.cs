namespace Emberdrop.Storage.Relational;

/// <summary>Settings shared by relational storage providers.</summary>
public class RelationalStoreOptions
{
    /// <summary>Connection string of the database that holds Emberdrop's tables.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Clock used to stamp creation and update times. Replace in tests to control time.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
