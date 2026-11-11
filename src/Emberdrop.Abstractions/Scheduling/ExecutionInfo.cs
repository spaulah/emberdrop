namespace Emberdrop.Scheduling;

/// <summary>A snapshot of one run.</summary>
/// <param name="Id">The run's identifier.</param>
/// <param name="Invocation">What the run executes.</param>
/// <param name="State">Current state.</param>
/// <param name="FinishedAt">When the run reached a terminal state, if it has.</param>
/// <param name="Error">The failure message when <paramref name="State"/> is <see cref="ExecutionState.Failed"/> or a retry is pending.</param>
public sealed record ExecutionInfo(
    ExecutionId Id,
    WorkInvocation Invocation,
    ExecutionState State,
    DateTimeOffset? FinishedAt = null,
    string? Error = null);
