namespace Emberdrop.WorkUnits;

/// <summary>What a host that runs work units decides, on top of how plugins are hosted (<c>PluginHostOptions</c>).</summary>
public sealed class WorkUnitOptions
{
    /// <summary>
    /// The trigger kinds this host understands when it reads manifests and API requests. It starts with the three built-in kinds;
    /// add a kind with <see cref="TriggerKindRegistry.Add"/> (see docs/concepts/triggers.md).
    /// </summary>
    public TriggerKindRegistry TriggerKinds { get; } = new();

    /// <summary>
    /// The work unit kinds this host can run. Starts with <c>work-unit</c>, the only kind the host knows how to run; a package that
    /// declares any other kind is rejected until the host adds it here and handles it.
    /// </summary>
    public IList<string> WorkUnitKinds { get; } = new List<string> { PluginWorkUnitManifest.DefaultKind };

    /// <summary>What a scheduled run does when the plugin version its work unit is bound to is not loaded.</summary>
    public MissingPluginPolicy MissingPlugin { get; set; } = MissingPluginPolicy.Wait;
}
