using Emberdrop.Plugins;
using Emberdrop.Storage;

namespace Emberdrop.WorkUnits;

/// <summary>Tells whether a stored work unit can run against the plugin versions that are loaded right now, and if not, why.</summary>
public interface IWorkUnitBindingResolver
{
    /// <summary>Resolves the work unit's binding. Never throws for a bad binding; the reason is in the result.</summary>
    WorkUnitBindingStatus Describe(WorkUnitDefinition workUnit);

    /// <summary>
    /// Checks that <paramref name="input"/> fits the input type of the plugin work unit <paramref name="workUnit"/> is bound to.
    /// Returns a message fit to show to a user, or <see langword="null"/> when it fits or there is nothing to check against (a work unit
    /// built into the host, or no loaded version yet).
    /// </summary>
    string? ValidateInput(WorkUnitDefinition workUnit, string? input);
}
