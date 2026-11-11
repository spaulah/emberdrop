namespace Emberdrop.Plugins;

/// <summary>Per-call overrides for retiring a plugin version. Anything left <see langword="null"/> uses <see cref="PluginHostOptions"/>.</summary>
public sealed record DrainOptions
{
    /// <summary>How long in-flight executions may finish on their own before they are asked to stop. Zero asks immediately.</summary>
    public TimeSpan? GracePeriod { get; init; }

    /// <summary>How long executions get to react to the cancellation request before the stuck policy applies.</summary>
    public TimeSpan? CancellationTimeout { get; init; }

    /// <summary>What to do with executions that outlast both periods.</summary>
    public StuckPolicy? StuckPolicy { get; init; }
}
