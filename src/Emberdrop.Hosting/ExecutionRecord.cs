namespace Emberdrop.Plugins;

/// <summary>Host-side bookkeeping for one execution of a plugin export. Holds no plugin types.</summary>
internal sealed class ExecutionRecord
{
    public ExecutionRecord(string exportName, CancellationTokenSource cancellation)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        ExportName = exportName;
        StartedAt = DateTimeOffset.UtcNow;
        Cancellation = cancellation;
    }

    public string Id { get; }

    public string ExportName { get; }

    public DateTimeOffset StartedAt { get; }

    public CancellationTokenSource Cancellation { get; }

    /// <summary>Completed when the host stops waiting for this execution; the awaiting side then reports it failed.</summary>
    public TaskCompletionSource AbandonSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CancellationRequested { get; set; }

    public bool Abandoned { get; set; }

    /// <summary>Whether the export's code has not returned yet. Meaningful for abandoned executions.</summary>
    public bool StillRunning { get; set; } = true;
}
