using System.Collections.Concurrent;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;

namespace Emberdrop.Providers.ContractTests.Scheduler;

/// <summary>
/// Stands in for the host's work unit invoker so tests can see exactly what an engine ran.
/// The work unit type picks the behavior: <c>fail</c> throws, <c>block</c> waits until released or cancelled,
/// anything else completes immediately.
/// </summary>
public sealed class RecordingInvoker : IWorkInvoker
{
    public const string Fail = "fail";
    public const string Block = "block";

    private readonly ConcurrentQueue<WorkInvocation> _calls = new();
    private readonly TaskCompletionSource _blockStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _blockSawCancellation;

    public IReadOnlyCollection<WorkInvocation> Calls => _calls.ToArray();

    public Task BlockStarted => _blockStarted.Task;

    public bool BlockedWorkUnitSawCancellation => Volatile.Read(ref _blockSawCancellation) == 1;

    public void ReleaseBlockedWorkUnit() => _release.TrySetResult();

    public async Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(invocation);

        switch (invocation.WorkUnitType)
        {
            case Fail:
                throw new InvalidOperationException("boom");
            case Block:
                _blockStarted.TrySetResult();
                try
                {
                    await _release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Volatile.Write(ref _blockSawCancellation, 1);
                    throw;
                }

                break;
        }

        return WorkResult.Succeeded();
    }
}
