using Emberdrop.Scheduling;

namespace Emberdrop.InMemory;

/// <summary>Mutable bookkeeping for one run. All access is guarded by the engine's lock.</summary>
internal sealed class InMemoryExecution
{
    public InMemoryExecution(ExecutionId id, WorkInvocation invocation, ExecutionState state)
    {
        Id = id;
        Invocation = invocation;
        State = state;
    }

    public ExecutionId Id { get; }
    public WorkInvocation Invocation { get; }
    public ExecutionState State { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public bool CancelRequested { get; set; }
    public ITimer? DelayTimer { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
    public Task? Running { get; set; }

    public ExecutionInfo Snapshot() => new(Id, Invocation, State, FinishedAt, Error);
}
