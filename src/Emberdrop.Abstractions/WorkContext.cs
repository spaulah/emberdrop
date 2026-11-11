using Emberdrop.Plugins;
using Microsoft.Extensions.Logging;

namespace Emberdrop;

/// <summary>Describes one execution of a work unit.</summary>
public sealed class WorkContext
{
    /// <summary>The identifier of this execution.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>The logical name of the work unit within its plugin (or its type name for a work unit built into the host).</summary>
    public required string WorkUnitName { get; init; }

    /// <summary>The id of the plugin that provides the work unit, or <see langword="null"/> for a work unit built into the host.</summary>
    public string? PluginId { get; init; }

    /// <summary>The plugin version the execution is bound to, or <see langword="null"/> for a work unit built into the host.</summary>
    public PluginVersion? PluginVersion { get; init; }

    /// <summary>What caused this execution.</summary>
    public required Trigger Trigger { get; init; }

    /// <summary>A logger for the execution.</summary>
    public required ILogger Logger { get; init; }

    /// <summary>A service provider scoped to this execution. For a plugin work unit it is the plugin's own container.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// The values of the configuration keys the manifest declares for the work unit, as set in the host. Keys the manifest does not declare are not visible.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Configuration { get; init; }

    /// <summary>
    /// The input of the execution as JSON, or <see langword="null"/> when there is none. <see cref="WorkUnit{TInput, TOutput}"/> reads it for you.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Lets a work unit tell the host about long-lived things it starts (threads, timers, subscriptions). Using it is optional;
    /// what is registered shows up in the diagnostic report if the plugin cannot be unloaded.
    /// </summary>
    public IPluginActivity? Activity { get; init; }
}
