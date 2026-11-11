namespace Emberdrop.WorkUnits;

/// <summary>Knows which work units are built into the host itself, as opposed to provided by plugins.</summary>
public interface IHostWorkUnitCatalog
{
    /// <summary>Whether the host has a built-in work unit with this name (class name or full type name).</summary>
    bool Provides(string workUnitKey);
}
