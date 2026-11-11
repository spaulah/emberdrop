namespace Emberdrop.Plugins;

/// <summary>
/// The legal moves between <see cref="PluginState"/> values. Every state change in the host goes through
/// <see cref="EnsureCanMove"/>, so an impossible transition is a bug that fails loudly instead of corrupting state.
/// </summary>
public static class PluginLifecycle
{
    private static readonly Dictionary<PluginState, PluginState[]> Allowed = new()
    {
        [PluginState.Staged] = [PluginState.Validated, PluginState.Failed, PluginState.Quarantined],
        [PluginState.Validated] = [PluginState.Active, PluginState.Failed, PluginState.Quarantined],
        [PluginState.Active] = [PluginState.Draining],
        [PluginState.Draining] = [PluginState.Unloading],
        [PluginState.Unloading] = [PluginState.Unloaded],
        [PluginState.Unloaded] = [],
        // A version that failed to activate can still have its package moved aside afterwards.
        [PluginState.Failed] = [PluginState.Quarantined],
        [PluginState.Quarantined] = [],
    };

    /// <summary>Whether a version in state <paramref name="from"/> may move to state <paramref name="to"/>.</summary>
    public static bool CanMove(PluginState from, PluginState to) => Allowed[from].Contains(to);

    /// <summary>Whether no further transition is possible out of <paramref name="state"/> except the ones listed for it.</summary>
    public static bool IsTerminal(PluginState state) => state is PluginState.Unloaded or PluginState.Failed or PluginState.Quarantined;

    /// <summary>Whether the version still occupies its id and version number (it is loading, loaded or leaving).</summary>
    public static bool IsLive(PluginState state) => !IsTerminal(state);

    internal static void EnsureCanMove(PluginState from, PluginState to)
    {
        if (!CanMove(from, to))
        {
            throw new InvalidOperationException($"A plugin version cannot move from {from} to {to}.");
        }
    }
}
