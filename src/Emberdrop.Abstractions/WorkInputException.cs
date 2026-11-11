namespace Emberdrop;

/// <summary>Thrown by <see cref="WorkUnit{TInput, TOutput}"/> when the execution's input does not fit the work unit's input type.</summary>
public sealed class WorkInputException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WorkInputException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying parsing error.</summary>
    public WorkInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
