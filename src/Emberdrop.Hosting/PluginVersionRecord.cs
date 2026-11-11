namespace Emberdrop.Plugins;

/// <summary>
/// Everything the host tracks for one plugin version. All fields are guarded by the host's lock.
/// The only reference to the plugin's types is <see cref="Plugin"/>, which is cleared when the version is released.
/// </summary>
internal sealed class PluginVersionRecord
{
    public PluginVersionRecord(string id, PluginVersion? version, string? location, PluginState state)
    {
        Id = id;
        Version = version;
        Location = location;
        State = state;
        StateSince = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public PluginVersion? Version { get; }

    public string? Location { get; set; }

    public PluginState State { get; private set; }

    public DateTimeOffset StateSince { get; private set; }

    public string? Reason { get; set; }

    public LoadedPlugin? Plugin { get; set; }

    public Dictionary<string, ExecutionRecord> Executions { get; } = [];

    public List<ExecutionRecord> AbandonedExecutions { get; } = [];

    public PluginActivityTracker Activities { get; } = new();

    public bool Stuck { get; set; }

    public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<UnloadResult>? UnloadTask { get; set; }

    public WeakReference? ContextReference { get; set; }

    public bool IsUnloaded => State == PluginState.Unloaded;

    public void MoveTo(PluginState next, string? reason = null)
    {
        PluginLifecycle.EnsureCanMove(State, next);
        State = next;
        StateSince = DateTimeOffset.UtcNow;
        if (reason is not null)
        {
            Reason = reason;
        }
    }

    public string Label => Version is null ? Id : $"{Id}@{Version}";
}
