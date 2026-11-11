using System.Text.Json;

namespace Emberdrop;

/// <summary>The outcome of one execution: succeeded (optionally with an output), or failed with an error code and message.</summary>
public sealed record WorkResult
{
    private WorkResult(bool isSuccess, string? outputJson, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        OutputJson = outputJson;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Whether the execution succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The output as JSON when the work unit produced one; otherwise <see langword="null"/>. Always <see langword="null"/> for a failure.</summary>
    public string? OutputJson { get; }

    /// <summary>A short, machine-readable error code for a failure; <see langword="null"/> for a success.</summary>
    public string? ErrorCode { get; }

    /// <summary>A human-readable description of a failure; <see langword="null"/> for a success.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Success without an output.</summary>
    public static WorkResult Succeeded() => new(true, null, null, null);

    /// <summary>Success with a typed output, serialized as JSON with <see cref="WorkJson.CreateOptions()"/>.</summary>
    public static WorkResult Succeeded<TOutput>(TOutput output) =>
        new(true, output is null or NoOutput ? null : JsonSerializer.Serialize(output, WorkJson.CreateOptions()), null, null);

    /// <summary>A failure the work unit reports itself, as opposed to throwing.</summary>
    /// <param name="errorCode">A short code, for example <c>smtp-unreachable</c>.</param>
    /// <param name="errorMessage">What went wrong, for an operator to read.</param>
    public static WorkResult Failed(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, null, errorCode, errorMessage);
    }
}
