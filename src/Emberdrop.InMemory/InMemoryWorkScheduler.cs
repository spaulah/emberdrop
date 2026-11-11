using Cronos;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.InMemory;

/// <summary>
/// A scheduler that keeps everything in process memory and drives it with timers.
/// Intended for demos, tests and single-node setups where losing schedules on restart is acceptable.
/// </summary>
/// <remarks>
/// Not durable and not safe to share between processes. Runs execute on the thread pool with no concurrency limit.
/// Failed runs are not retried.
/// </remarks>
public sealed class InMemoryWorkScheduler : IWorkScheduler, IAsyncDisposable, IDisposable
{
    private const SchedulerFeatures Declared =
        SchedulerFeatures.Recurring
        | SchedulerFeatures.Delayed
        | SchedulerFeatures.CancelQueued
        | SchedulerFeatures.CancelRunning
        | SchedulerFeatures.History;

    /// <summary>Timers cannot wait arbitrarily long, so long waits are taken in steps.</summary>
    private static readonly TimeSpan MaxTimerStep = TimeSpan.FromDays(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, RecurringEntry> _recurring = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InMemoryExecution> _executions = new(StringComparer.Ordinal);
    private readonly LinkedList<InMemoryExecution> _finished = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<InMemoryWorkScheduler> _logger;
    private readonly InMemorySchedulerOptions _options;
    private readonly TimeProvider _time;
    private bool _disposed;

    /// <summary>Creates the scheduler.</summary>
    public InMemoryWorkScheduler(IServiceScopeFactory scopes, InMemorySchedulerOptions options, ILogger<InMemoryWorkScheduler> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
        _time = options.TimeProvider;
    }

    /// <inheritdoc />
    public SchedulerCapabilities Capabilities { get; } = new("InMemory", Declared);

    /// <inheritdoc />
    public Task UpsertRecurringAsync(RecurringWorkSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // A disabled entry is kept (so it can still be triggered) but its cron is never parsed or armed.
        var cron = spec.Enabled ? CronExpression.Parse(spec.Cron) : null;
        var entry = new RecurringEntry(spec, cron);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_recurring.Remove(spec.ScheduleKey, out var previous))
            {
                Retire(previous);
            }

            _recurring[spec.ScheduleKey] = entry;
            Arm(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveRecurringAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_recurring.Remove(scheduleKey, out var entry))
            {
                Retire(entry);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SchedulerResult<ExecutionId>> TriggerAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_recurring.TryGetValue(scheduleKey, out var entry))
            {
                return Task.FromResult(SchedulerResult.NotFound<ExecutionId>());
            }

            return Task.FromResult(SchedulerResult.Ok(StartExecution(entry.Spec.Invocation).Id));
        }
    }

    /// <inheritdoc />
    public Task<ExecutionId> EnqueueAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        lock (_gate)
        {
            ThrowIfDisposed();
            return Task.FromResult(StartExecution(invocation).Id);
        }
    }

    /// <inheritdoc />
    public Task<SchedulerResult<ExecutionId>> ScheduleAsync(WorkInvocation invocation, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        lock (_gate)
        {
            ThrowIfDisposed();
            var run = NewExecution(invocation, ExecutionState.Scheduled);
            ArmDelayed(run, runAt);
            return Task.FromResult(SchedulerResult.Ok(run.Id));
        }
    }

    /// <inheritdoc />
    public Task<SchedulerResult<bool>> CancelAsync(ExecutionId runId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_executions.TryGetValue(runId.Value, out var run))
            {
                return Task.FromResult(SchedulerResult.NotFound<bool>());
            }

            switch (run.State)
            {
                case ExecutionState.Queued:
                case ExecutionState.Scheduled:
                    run.DelayTimer?.Dispose();
                    run.DelayTimer = null;
                    run.CancelRequested = true;
                    run.Cancellation?.Cancel();
                    Finish(run, ExecutionState.Canceled, null);
                    return Task.FromResult(SchedulerResult.Ok(true));
                case ExecutionState.Running:
                    run.CancelRequested = true;
                    run.Cancellation?.Cancel();
                    Finish(run, ExecutionState.Canceled, null);
                    return Task.FromResult(SchedulerResult.Ok(true));
                default:
                    return Task.FromResult(SchedulerResult.Ok(false));
            }
        }
    }

    /// <inheritdoc />
    public Task<ExecutionInfo?> GetRunAsync(ExecutionId runId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_executions.TryGetValue(runId.Value, out var run) ? run.Snapshot() : null);
        }
    }

    /// <inheritdoc />
    public Task<SchedulerResult<IReadOnlyList<ExecutionInfo>>> GetHistoryAsync(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
        {
            // _finished is kept newest-first.
            IReadOnlyList<ExecutionInfo> page = _finished
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(r => r.Snapshot())
                .ToList();
            return Task.FromResult(SchedulerResult.Ok(page));
        }
    }

    /// <summary>Stops all timers and cancels running work units without waiting for them to stop.</summary>
    public void Dispose() => Stop();

    /// <summary>Stops all timers, cancels running work units and waits briefly for them to stop.</summary>
    public async ValueTask DisposeAsync()
    {
        var running = Stop();

        try
        {
            await Task.WhenAll(running).WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("In-memory scheduler stopped while {Count} run(s) were still shutting down", running.Count(t => !t.IsCompleted));
        }
        catch (Exception)
        {
            // Work unit failures were already recorded on the runs; shutdown only cares that they stopped.
        }
    }

    /// <summary>Stops all timers and cancels unfinished runs. Returns the executions still in flight. Safe to call twice.</summary>
    private List<Task> Stop()
    {
        var running = new List<Task>();
        lock (_gate)
        {
            if (_disposed)
            {
                return running;
            }

            _disposed = true;
            foreach (var entry in _recurring.Values)
            {
                Retire(entry);
            }

            _recurring.Clear();

            foreach (var run in _executions.Values.Where(r => r.FinishedAt is null))
            {
                run.DelayTimer?.Dispose();
                run.Cancellation?.Cancel();
                if (run.Running is { IsCompleted: false })
                {
                    running.Add(run.Running);
                }
            }
        }

        return running;
    }

    private static void Retire(RecurringEntry entry)
    {
        entry.Removed = true;
        entry.Timer?.Dispose();
        entry.Timer = null;
    }

    private void Arm(RecurringEntry entry)
    {
        if (entry.Cron is null || entry.Removed || _disposed)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var next = entry.Cron.GetNextOccurrence(now.UtcDateTime, TimeZoneInfo.Utc);
        if (next is null)
        {
            entry.NextFire = null;
            return;
        }

        entry.NextFire = new DateTimeOffset(next.Value, TimeSpan.Zero);
        var wait = entry.NextFire.Value - now;
        var step = wait < MaxTimerStep ? wait : MaxTimerStep;

        entry.Timer?.Dispose();
        entry.Timer = _time.CreateTimer(_ => OnRecurringTimer(entry), null, step, Timeout.InfiniteTimeSpan);
    }

    private void OnRecurringTimer(RecurringEntry entry)
    {
        lock (_gate)
        {
            if (entry.Removed || _disposed)
            {
                return;
            }

            if (entry.NextFire is { } due && _time.GetUtcNow() >= due)
            {
                StartExecution(entry.Spec.Invocation);
            }

            Arm(entry);
        }
    }

    private void ArmDelayed(InMemoryExecution run, DateTimeOffset runAt)
    {
        var wait = runAt - _time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            Begin(run);
            return;
        }

        var step = wait < MaxTimerStep ? wait : MaxTimerStep;
        run.DelayTimer?.Dispose();
        run.DelayTimer = _time.CreateTimer(_ => OnDelayedTimer(run, runAt), null, step, Timeout.InfiniteTimeSpan);
    }

    private void OnDelayedTimer(InMemoryExecution run, DateTimeOffset runAt)
    {
        lock (_gate)
        {
            if (run.State != ExecutionState.Scheduled || _disposed)
            {
                return;
            }

            ArmDelayed(run, runAt);
        }
    }

    private InMemoryExecution NewExecution(WorkInvocation invocation, ExecutionState state)
    {
        var run = new InMemoryExecution(new ExecutionId(Guid.NewGuid().ToString("N")), invocation, state);
        _executions[run.Id.Value] = run;
        return run;
    }

    private InMemoryExecution StartExecution(WorkInvocation invocation)
    {
        var run = NewExecution(invocation, ExecutionState.Queued);
        Begin(run);
        return run;
    }

    /// <summary>Moves a run to Running and starts executing it on the thread pool. Caller holds the lock.</summary>
    private void Begin(InMemoryExecution run)
    {
        run.DelayTimer?.Dispose();
        run.DelayTimer = null;
        run.State = ExecutionState.Running;
        run.Cancellation = new CancellationTokenSource();
        var token = run.Cancellation.Token;
        run.Running = Task.Run(() => ExecuteAsync(run, token), CancellationToken.None);
    }

    private async Task ExecuteAsync(InMemoryExecution run, CancellationToken cancellationToken)
    {
        try
        {
            var scope = _scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var invoker = scope.ServiceProvider.GetRequiredService<IWorkInvoker>();
                await invoker.InvokeAsync(run.Invocation, cancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                Finish(run, ExecutionState.Succeeded, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                Finish(run, ExecutionState.Canceled, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} of work unit {WorkUnitType} ({WorkUnitId}) failed", run.Id, run.Invocation.WorkUnitType, run.Invocation.WorkUnitId);
            lock (_gate)
            {
                Finish(run, ExecutionState.Failed, ex.Message);
            }
        }
        finally
        {
            lock (_gate)
            {
                run.Cancellation?.Dispose();
            }
        }
    }

    /// <summary>Records a terminal state once. A cancellation request wins over a later outcome. Caller holds the lock.</summary>
    private void Finish(InMemoryExecution run, ExecutionState state, string? error)
    {
        if (run.FinishedAt is not null)
        {
            return;
        }

        run.State = run.CancelRequested ? ExecutionState.Canceled : state;
        run.Error = run.CancelRequested ? null : error;
        run.FinishedAt = _time.GetUtcNow();

        _finished.AddFirst(run);
        while (_finished.Count > Math.Max(1, _options.MaxFinishedRuns))
        {
            var oldest = _finished.Last!.Value;
            _finished.RemoveLast();
            _executions.Remove(oldest.Id.Value);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
