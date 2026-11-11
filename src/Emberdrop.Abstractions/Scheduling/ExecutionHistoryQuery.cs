namespace Emberdrop.Scheduling;

/// <summary>Selects a page of finished runs, most recently finished first.</summary>
public sealed record ExecutionHistoryQuery
{
    private readonly int _skip;
    private readonly int _take = 50;

    /// <summary>Number of runs to skip.</summary>
    public int Skip
    {
        get => _skip;
        init => _skip = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Skip cannot be negative.");
    }

    /// <summary>Maximum number of runs to return.</summary>
    public int Take
    {
        get => _take;
        init => _take = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Take must be positive.");
    }
}
