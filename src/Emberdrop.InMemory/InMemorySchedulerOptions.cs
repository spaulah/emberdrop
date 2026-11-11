namespace Emberdrop.InMemory;

/// <summary>Settings for the in-memory scheduler.</summary>
public sealed class InMemorySchedulerOptions
{
    /// <summary>How many finished runs are kept for status and history queries. Older runs are dropped first.</summary>
    public int MaxFinishedRuns { get; set; } = 1000;

    /// <summary>How long disposing the scheduler waits for running work units to observe cancellation.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Clock used for all timing. Replace in tests to control time.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
