using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Keeps stored work units and their schedules in step with the plugins that are loaded.
/// </summary>
/// <remarks>
/// Rules, in order of importance:
/// <list type="number">
/// <item><description>An operator's choices are never overwritten by a plugin: the schedule override, the enabled flag, the version constraint and the input stay as they are.</description></item>
/// <item><description>A plugin update only refreshes the plugin-owned fields: the default schedule, and the effective schedule when no override exists.</description></item>
/// <item><description>Nothing is ever deleted. When a plugin's last active version goes away, its work units' schedules are paused (removed from the scheduler, kept in the store) and they resume when it returns.</description></item>
/// <item><description>A work unit a new version no longer declares is left in place and reported as waiting; the operator decides.</description></item>
/// </list>
/// </remarks>
public sealed class PluginWorkUnitReconciler : IPluginLifecycleListener, IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WaitingInvocations _waiting;
    private readonly IEnumerable<IHostWorkUnitCatalog> _hostWorkUnits;
    private readonly ILogger<PluginWorkUnitReconciler> _logger;
    private readonly SemaphoreSlim _serialize = new(1, 1);

    /// <summary>Creates the reconciler.</summary>
    public PluginWorkUnitReconciler(
        IServiceScopeFactory scopes,
        WaitingInvocations waiting,
        IEnumerable<IHostWorkUnitCatalog> hostWorkUnits,
        ILogger<PluginWorkUnitReconciler> logger)
    {
        _scopes = scopes;
        _waiting = waiting;
        _hostWorkUnits = hostWorkUnits;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PluginVersionsChangedAsync(string pluginId, IReadOnlyList<PluginDescriptor> activeVersions, CancellationToken cancellationToken)
    {
        // One change at a time: two plugins arriving together must not create the same stored work unit twice.
        await _serialize.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkStore>();
            var scheduling = scope.ServiceProvider.GetRequiredService<IWorkSchedulerService>();
            var resolver = scope.ServiceProvider.GetRequiredService<IWorkUnitBindingResolver>();
            var scheduler = scope.ServiceProvider.GetRequiredService<IWorkScheduler>();
            var configuration = scope.ServiceProvider.GetRequiredService<PluginServiceProviderFactory>();

            if (activeVersions.Count > 0)
            {
                await SyncDefinitionsAsync(store, configuration, pluginId, activeVersions[0], cancellationToken).ConfigureAwait(false);
            }

            // Apply the desired scheduler state to every work unit of this plugin: schedule it if it can run, pause it if it cannot.
            foreach (var workUnit in (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(j => j.PluginId == pluginId))
            {
                await scheduling.ScheduleWorkUnitAsync(workUnit, cancellationToken).ConfigureAwait(false);
            }

            await RepeatWaitingRunsAsync(store, resolver, scheduler, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _serialize.Release();
        }
    }

    private async Task SyncDefinitionsAsync(IWorkStore store, PluginServiceProviderFactory configuration, string pluginId, PluginDescriptor newest, CancellationToken cancellationToken)
    {
        var workUnits = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).ToList();

        foreach (var declared in newest.WorkUnits())
        {
            var row = workUnits.FirstOrDefault(j => j.PluginId == pluginId && j.WorkUnitName == declared.Name) ?? Adoptable(workUnits, declared);
            var missing = configuration.MissingConfiguration(declared.RequiredConfiguration);
            if (missing.Count > 0)
            {
                _logger.LogWarning("Work unit {WorkUnit} of plugin {PluginId} {Version} needs configuration that is not set: {Keys}. It will not run until it is.",
                    declared.Name, pluginId, newest.Version, string.Join(", ", missing));
            }

            if (row is null)
            {
                var created = NewDefinition(pluginId, newest, declared, enabled: declared.DefaultSchedule is not null && missing.Count == 0);
                created.WorkUnitId = await store.AddAsync(created, cancellationToken).ConfigureAwait(false);
                workUnits.Add(created);
                _logger.LogInformation("Registered work unit {WorkUnit} of plugin {PluginId} (default schedule: {Schedule}, enabled: {Enabled})",
                    declared.Name, pluginId, declared.DefaultSchedule ?? "none", created.Enabled);
                continue;
            }

            if (UpdatePluginOwnedFields(row, pluginId, declared))
            {
                await store.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private WorkUnitDefinition? Adoptable(List<WorkUnitDefinition> workUnits, PluginWorkUnitDescriptor declared) =>
        workUnits.FirstOrDefault(j =>
            string.IsNullOrWhiteSpace(j.PluginId)
            && j.Type is { Length: > 0 } type
            && (type == declared.Name || type == declared.TypeName || type == BindingResolution.SimpleName(declared.TypeName))
            && !_hostWorkUnits.Any(h => h.Provides(type)));

    private static WorkUnitDefinition NewDefinition(string pluginId, PluginDescriptor plugin, PluginWorkUnitDescriptor declared, bool enabled) => new()
    {
        Identifier = Identifier(pluginId, declared.Name),
        Name = declared.Name,
        Type = declared.Name,
        PluginId = pluginId,
        WorkUnitName = declared.Name,
        // New work units follow their plugin within the same major version; moving to the next major is the operator's decision.
        VersionConstraint = $"^{plugin.Version.Major}.0.0",
        InputSchemaVersion = declared.InputSchemaVersion,
        DefaultSchedule = declared.DefaultSchedule,
        ScheduleOverride = null,
        Kind = declared.Kind,
        Description = declared.Description,
        Capabilities = CapabilitiesJson(declared),
        InputSchema = declared.InputSchema,
        OutputSchema = declared.OutputSchema,
        Schedule = declared.DefaultSchedule ?? WorkUnitScheduleDefaults.NeverSchedule,
        Enabled = enabled,
    };

    /// <summary>Updates only what the plugin owns. Returns whether anything changed.</summary>
    private static bool UpdatePluginOwnedFields(WorkUnitDefinition row, string pluginId, PluginWorkUnitDescriptor declared)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(row.PluginId))
        {
            // Adopting a work unit stored before bindings existed. A schedule an operator had set by hand becomes an override,
            // because it is the operator's and must survive.
            if (row.Schedule is { Length: > 0 } existing && existing != WorkUnitScheduleDefaults.NeverSchedule && existing != declared.DefaultSchedule)
            {
                row.ScheduleOverride = existing;
            }

            row.PluginId = pluginId;
            row.WorkUnitName = declared.Name;
            row.Type = declared.Name;
            row.VersionConstraint = string.IsNullOrWhiteSpace(row.VersionConstraint) ? null : row.VersionConstraint;
            changed = true;
        }

        if (row.DefaultSchedule != declared.DefaultSchedule)
        {
            row.DefaultSchedule = declared.DefaultSchedule;
            changed = true;
        }

        // Everything the manifest and the plugin's types describe follows the newest plugin version; none of it is the operator's.
        var capabilities = CapabilitiesJson(declared);
        if (row.Kind != declared.Kind || row.Description != declared.Description || row.Capabilities != capabilities
            || row.InputSchema != declared.InputSchema || row.OutputSchema != declared.OutputSchema)
        {
            (row.Kind, row.Description, row.Capabilities, row.InputSchema, row.OutputSchema) =
                (declared.Kind, declared.Description, capabilities, declared.InputSchema, declared.OutputSchema);
            changed = true;
        }

        var effective = row.ScheduleOverride ?? row.DefaultSchedule;
        if (effective is not null && row.Schedule != effective)
        {
            row.Schedule = effective;
            changed = true;
        }

        return changed;
    }

    private async Task RepeatWaitingRunsAsync(IWorkStore store, IWorkUnitBindingResolver resolver, IWorkScheduler scheduler, CancellationToken cancellationToken)
    {
        foreach (var workUnitId in _waiting.WorkUnitIds)
        {
            var workUnit = await store.GetAsync(workUnitId, cancellationToken).ConfigureAwait(false);
            if (workUnit is null)
            {
                _waiting.TryTake(workUnitId, out _);
                continue;
            }

            if (resolver.Describe(workUnit).State == WorkUnitBindingState.Ready && _waiting.TryTake(workUnitId, out var invocation))
            {
                await scheduler.EnqueueAsync(invocation, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Repeating the run of work unit {WorkUnit} (id {WorkUnitId}) that was waiting for its plugin", workUnit.Name, workUnitId);
            }
        }
    }

    private static string? CapabilitiesJson(PluginWorkUnitDescriptor declared) =>
        declared.Capabilities is { Count: > 0 } capabilities ? System.Text.Json.JsonSerializer.Serialize(capabilities) : null;

    private static string Identifier(string pluginId, string workUnitName)
    {
        // Readable prefix plus a unique suffix, so two plugins' work units with the same name never share a scheduler key.
        var readable = $"{pluginId}-{workUnitName}";
        var prefix = readable.Length > 40 ? readable[..40] : readable;
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public void Dispose() => _serialize.Dispose();
}
