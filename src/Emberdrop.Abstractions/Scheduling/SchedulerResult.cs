namespace Emberdrop.Scheduling;

/// <summary>
/// The result of a scheduler operation that may be refused for an expected reason.
/// Engines return <see cref="SchedulerResultStatus.Unsupported"/> for a feature they did not declare in
/// <see cref="SchedulerCapabilities"/> instead of throwing.
/// </summary>
/// <typeparam name="T">The value produced when the operation succeeds.</typeparam>
public readonly record struct SchedulerResult<T>
{
    internal SchedulerResult(SchedulerResultStatus status, T? value, SchedulerFeatures missingFeature)
    {
        Status = status;
        Value = value;
        MissingFeature = missingFeature;
    }

    /// <summary>What happened.</summary>
    public SchedulerResultStatus Status { get; }

    /// <summary>The produced value; meaningful only when <see cref="Status"/> is <see cref="SchedulerResultStatus.Ok"/>.</summary>
    public T? Value { get; }

    /// <summary>The feature that was needed but not declared; set only when <see cref="Status"/> is <see cref="SchedulerResultStatus.Unsupported"/>.</summary>
    public SchedulerFeatures MissingFeature { get; }

    /// <summary>Whether the operation was carried out.</summary>
    public bool IsOk => Status == SchedulerResultStatus.Ok;
}

/// <summary>Creates <see cref="SchedulerResult{T}"/> values.</summary>
public static class SchedulerResult
{
    /// <summary>Creates a successful result.</summary>
    public static SchedulerResult<T> Ok<T>(T value) => new(SchedulerResultStatus.Ok, value, SchedulerFeatures.None);

    /// <summary>Creates a not-found result.</summary>
    public static SchedulerResult<T> NotFound<T>() => new(SchedulerResultStatus.NotFound, default, SchedulerFeatures.None);

    /// <summary>Creates an unsupported result naming the missing feature.</summary>
    public static SchedulerResult<T> Unsupported<T>(SchedulerFeatures missingFeature) =>
        new(SchedulerResultStatus.Unsupported, default, missingFeature);
}
