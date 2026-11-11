namespace Emberdrop.WorkUnits;

/// <summary>
/// Thrown by <see cref="IWorkInvoker"/> when a work unit returns <see cref="WorkResult.Failed"/>, so that scheduler engines
/// record the execution as failed (and retry it where they support that) exactly as they would for a thrown exception.
/// </summary>
public sealed class WorkUnitFailedException : Exception
{
    /// <summary>Creates the exception from a failed result.</summary>
    public WorkUnitFailedException(string workUnitName, string errorCode, string errorMessage)
        : base($"Work unit '{workUnitName}' failed with {errorCode}: {errorMessage}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>The error code the work unit reported.</summary>
    public string ErrorCode { get; }
}
