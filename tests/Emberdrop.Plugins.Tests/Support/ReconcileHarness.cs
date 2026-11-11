using System.Collections.Concurrent;
using Emberdrop.Hosting;
using Emberdrop.InMemory;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Plugins.Tests.Support;

/// <summary>A scheduler double that remembers which recurring entries exist and runs enqueued work, so tests can see exactly what the system asked of the scheduler.</summary>
internal sealed class RecordingScheduler : IWorkScheduler
{
    private readonly ConcurrentDictionary<string, RecurringWorkSpec> _entries = new();
    private readonly IServiceScopeFactory _scopes;

    public RecordingScheduler(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public IReadOnlyDictionary<string, RecurringWorkSpec> Entries => _entries;

    public SchedulerCapabilities Capabilities { get; } = new("Recording", SchedulerFeatures.Recurring);

    public Task UpsertRecurringAsync(RecurringWorkSpec spec, CancellationToken cancellationToken = default)
    {
        _entries[spec.ScheduleKey] = spec;
        return Task.CompletedTask;
    }

    public Task RemoveRecurringAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(scheduleKey, out _);
        return Task.CompletedTask;
    }

    public Task<SchedulerResult<ExecutionId>> TriggerAsync(string scheduleKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(SchedulerResult.NotFound<ExecutionId>());

    public Task<ExecutionId> EnqueueAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWorkInvoker>().InvokeAsync(invocation, CancellationToken.None);
        }, CancellationToken.None);
        return Task.FromResult(new ExecutionId(Guid.NewGuid().ToString("N")));
    }

    public Task<SchedulerResult<ExecutionId>> ScheduleAsync(WorkInvocation invocation, DateTimeOffset runAt, CancellationToken cancellationToken = default) =>
        Task.FromResult(SchedulerResult.Unsupported<ExecutionId>(SchedulerFeatures.Delayed));

    public Task<SchedulerResult<bool>> CancelAsync(ExecutionId runId, CancellationToken cancellationToken = default) =>
        Task.FromResult(SchedulerResult.NotFound<bool>());

    public Task<ExecutionInfo?> GetRunAsync(ExecutionId runId, CancellationToken cancellationToken = default) => Task.FromResult<ExecutionInfo?>(null);

    public Task<SchedulerResult<IReadOnlyList<ExecutionInfo>>> GetHistoryAsync(ExecutionHistoryQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(SchedulerResult.Unsupported<IReadOnlyList<ExecutionInfo>>(SchedulerFeatures.History));
}

/// <summary>The part of the server's invoker that matters here: load the stored work unit, resolve its binding, run it through the plugin runner.</summary>
internal sealed class TestInvoker : IWorkInvoker
{
    private readonly IWorkStore _store;
    private readonly PluginWorkUnitRunner _runner;

    public TestInvoker(IWorkStore store, PluginWorkUnitRunner runner)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        var workUnit = await _store.GetAsync(invocation.WorkUnitId, cancellationToken) ?? throw new InvalidOperationException("No such stored work unit: " + invocation.WorkUnitId);
        return await _runner.RunAsync(workUnit, new WorkRequest(workUnit.Input, invocation.Trigger), cancellationToken);
    }
}

internal sealed class FakeHostWorkUnits(params string[] provided) : IHostWorkUnitCatalog
{
    public bool Provides(string workUnitKey) => provided.Contains(workUnitKey);
}

/// <summary>The real store, binding resolver, reconciler, runner and plugin host wired together the way the server wires them, with a recording scheduler.</summary>
internal sealed class ReconcileHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private ReconcileHarness(ServiceProvider provider)
    {
        _provider = provider;
    }

    public PluginHost Host => _provider.GetRequiredService<PluginHost>();

    public IWorkStore Store => _provider.GetRequiredService<IWorkStore>();

    public RecordingScheduler Scheduler => (RecordingScheduler)_provider.GetRequiredService<IWorkScheduler>();

    public PluginWorkUnitRunner Runner => _provider.GetRequiredService<PluginWorkUnitRunner>();

    public WaitingInvocations Waiting => _provider.GetRequiredService<WaitingInvocations>();

    public async Task FireAsync(WorkUnitDefinition workUnit)
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkInvoker>().InvokeAsync(new WorkInvocation(workUnit.Type!, workUnit.WorkUnitId));
    }

    public static ReconcileHarness Create(
        TempDirectory temp,
        Dictionary<string, string?>? configuration = null,
        Action<WorkUnitOptions>? configureOptions = null,
        params string[] hostWorkUnits)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build());
        services.AddScoped<IWorkInvoker, TestInvoker>();
        services.AddSingleton<IHostWorkUnitCatalog>(new FakeHostWorkUnits(hostWorkUnits));
        services.AddEmberdrop(b => b
            .UseScheduler<RecordingScheduler>(ServiceLifetime.Singleton)
            .UseInMemoryStore()
            .ConfigurePlugins(o =>
            {
                o.StorageRoot = Path.Combine(temp.Path, "plugins");
                o.SharedAssemblies.Add("Fixture.Contracts");
                o.UnloadVerificationAttempts = 20;
                o.UnloadVerificationDelay = TimeSpan.FromMilliseconds(20);
            })
            .ConfigureWorkUnits(o => configureOptions?.Invoke(o)));
        return new ReconcileHarness(services.BuildServiceProvider());
    }

    public async Task<WorkUnitDefinition> WorkUnitAsync(string pluginId, string workUnitName)
    {
        var workUnits = await Store.ListAsync();
        return workUnits.Single(j => j.PluginId == pluginId && j.WorkUnitName == workUnitName);
    }

    public Task SetAsync(WorkUnitDefinition workUnit) => Store.UpdateAsync(workUnit);

    public WorkUnitBindingStatus Describe(WorkUnitDefinition workUnit)
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IWorkUnitBindingResolver>().Describe(workUnit);
    }

    public async Task ScheduleAsync(WorkUnitDefinition workUnit)
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkSchedulerService>().ScheduleWorkUnitAsync(workUnit);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
