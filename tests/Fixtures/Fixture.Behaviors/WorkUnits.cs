using Emberdrop;
using Fixture.Contracts;

namespace Fixture.Behaviors;

internal static class Build
{
#if V2
    public const string Version = "v2";
#else
    public const string Version = "v1";
#endif
}

/// <summary>Waits at a named gate. Honors cancellation: it stops when asked.</summary>
public sealed class GateWorkUnit : IWorkUnit
{
    public async Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        var name = context.Input!;
        FixtureControl.Record(name + ":context", $"{context.PluginId}/{context.PluginVersion}/{context.WorkUnitName}");
        FixtureControl.SignalStarted(name);
        try
        {
            await FixtureControl.Gate(name).Task.WaitAsync(cancellationToken);
            FixtureControl.Record(name, "completed on " + Build.Version);
        }
        catch (OperationCanceledException)
        {
            FixtureControl.Record(name, "cancelled on " + Build.Version);
            throw;
        }

        return WorkResult.Succeeded();
    }
}

/// <summary>Waits at a named gate and ignores cancellation, like badly written real-world code.</summary>
public sealed class StubbornWorkUnit : IWorkUnit
{
    public async Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        var name = context.Input!;
        FixtureControl.SignalStarted(name);
        await FixtureControl.Gate(name).Task;
        FixtureControl.Record(name, "stubborn finished on " + Build.Version);
        return WorkResult.Succeeded();
    }
}

/// <summary>A very short work unit that counts its own executions by id, so a test can prove none were lost or repeated.</summary>
public sealed class CountWorkUnit : IWorkUnit
{
    public async Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();
        FixtureControl.Count(context.Input!, context.PluginVersion?.ToString() ?? "none");
        return WorkResult.Succeeded();
    }
}

/// <summary>Subscribes to a static event on a host-side type and never unsubscribes. That keeps the plugin's load context alive.</summary>
public sealed class StaticEventWorkUnit : IWorkUnit
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        FixtureControl.HostEvent += OnHostEvent;
        return Task.FromResult(WorkResult.Succeeded());
    }

    private void OnHostEvent(object? sender, EventArgs e)
    {
    }
}

/// <summary>Starts a background thread that outlives the execution, and declares it through the optional activity hook.</summary>
public sealed class ThreadWorkUnit : IWorkUnit
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        var name = context.Input!;
        var registration = context.Activity?.Track("thread", "worker for " + name);
        var thread = new Thread(() =>
        {
            while (!FixtureControl.ShouldStop(name))
            {
                Thread.Sleep(10);
            }

            registration?.Dispose();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return Task.FromResult(WorkResult.Succeeded());
    }
}

/// <summary>Always fails. Used to prove that a failing work unit's exception does not keep the plugin alive.</summary>
public sealed class FailWorkUnit : IWorkUnit
{
    public async Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom from " + Build.Version);
    }
}

/// <summary>Registers the plugin's own services. The host calls this when it builds the plugin's private service provider.</summary>
public sealed class Startup
{
    public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ReportBuilder>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<ScopedStamp>(services);
    }
}

/// <summary>A startup whose registrations cannot be satisfied, so the provider cannot be built.</summary>
public sealed class BrokenStartup
{
    public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<NeedsSomethingMissing>(services);
    }
}

public sealed class MissingDependency
{
}

public sealed class NeedsSomethingMissing
{
    public NeedsSomethingMissing(MissingDependency dependency)
    {
    }
}

/// <summary>A singleton of the plugin's provider. Its disposal shows that the provider is disposed when the plugin unloads.</summary>
public sealed class ReportBuilder : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public void Dispose() => FixtureControl.Mark("builder-disposed:" + Id);
}

/// <summary>A scoped service. Its disposal shows that every execution gets, and ends, its own scope.</summary>
public sealed class ScopedStamp : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public void Dispose() => FixtureControl.Mark("stamp-disposed:" + Id);
}

/// <summary>Uses the plugin's own services, a logger for its own type and a restricted view of configuration, and reports what it saw.</summary>
public sealed class ServiceWorkUnit : IWorkUnit
{
    private readonly Microsoft.Extensions.Logging.ILogger<ServiceWorkUnit> _logger;
    private readonly ReportBuilder _builder;
    private readonly ScopedStamp _stamp;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IServiceProvider _services;

    public ServiceWorkUnit(
        Microsoft.Extensions.Logging.ILogger<ServiceWorkUnit> logger,
        ReportBuilder builder,
        ScopedStamp stamp,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IServiceProvider services)
    {
        _logger = logger;
        _builder = builder;
        _stamp = stamp;
        _configuration = configuration;
        _services = services;
    }

    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        var name = context.Input!;
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "service work unit ran for {Name}", name);
        FixtureControl.Record(name + ":builder", _builder.Id);
        FixtureControl.Record(name + ":stamp", _stamp.Id);
        FixtureControl.Record(name + ":setting", _configuration["Fixture:Setting"] ?? "(null)");
        FixtureControl.Record(name + ":secret", _configuration["Secret:Key"] ?? "(null)");
        FixtureControl.Record(
            name + ":context",
            $"trigger={context.Trigger.ToToken()}; setting={context.Configuration.GetValueOrDefault("Fixture:Setting")}; secretVisible={context.Configuration.ContainsKey("Secret:Key")}; " +
            $"execution={(context.ExecutionId.Length > 0)}; plugin={context.PluginId}/{context.PluginVersion}; name={context.WorkUnitName}; sameServices={ReferenceEquals(context.Services, _services)}");
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(context.Logger, "context logger used for {Name}", name);
        var hostOnly = (IHostOnlyService?)_services.GetService(typeof(IHostOnlyService));
        FixtureControl.Record(name + ":host-only", hostOnly?.Secret ?? "(not visible)");
        return Task.FromResult(WorkResult.Succeeded());
    }
}

/// <summary>Does nothing; its manifest entry expects a different schema version of the stored input.</summary>
public sealed class SchemaTwoWorkUnit : IWorkUnit
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        FixtureControl.Mark("schema2-ran:" + context.Input);
        return Task.FromResult(WorkResult.Succeeded());
    }
}

/// <summary>The typed input of <see cref="EchoWorkUnit"/>.</summary>
public sealed record EchoInput(string Name, int Times);

/// <summary>The typed output of <see cref="EchoWorkUnit"/>.</summary>
public sealed record EchoOutput(string Greeting);

/// <summary>A work unit with typed input and output, for proving schemas, strict input and that typed JSON handling does not keep a plugin alive.</summary>
public sealed class EchoWorkUnit : WorkUnit<EchoInput, EchoOutput>
{
    protected override Task<EchoOutput> ExecuteAsync(EchoInput input, WorkContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoOutput(string.Join(' ', Enumerable.Repeat("hello " + input.Name + " from " + Build.Version, input.Times))));
}

/// <summary>Reports an expected failure instead of throwing.</summary>
public sealed class ReportsFailureWorkUnit : IWorkUnit
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken) =>
        Task.FromResult(WorkResult.Failed("expected-failure", "This work unit always reports a failure."));
}
