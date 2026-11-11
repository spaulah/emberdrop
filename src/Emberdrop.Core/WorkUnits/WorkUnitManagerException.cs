namespace Emberdrop.WorkUnits;

/// <summary>Thrown when a work unit management operation fails; the original failure is the inner exception.</summary>
public class WorkUnitManagerException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public WorkUnitManagerException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    public WorkUnitManagerException(string message, Exception innerException) : base(message, innerException) { }
}
