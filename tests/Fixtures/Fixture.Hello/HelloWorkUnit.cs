using System.Runtime.Loader;
using Emberdrop;
using Fixture.Contracts;

namespace Fixture.Hello;

/// <summary>The same source builds into version 1 and version 2 of the plugin; the greeting differs by build.</summary>
public sealed class HelloWorkUnit : IWorkUnit, IProbe
{
#if V2
    private const string Greeting = "hello from v2";
#else
    private const string Greeting = "hello from v1";
#endif

    private string _startedWith = string.Empty;

    private string _ranAs = string.Empty;

    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        _startedWith = context.Input ?? string.Empty;
        _ranAs = $"{context.PluginId}/{context.PluginVersion}/{context.WorkUnitName}";
        return Task.FromResult(WorkResult.Succeeded());
    }

    public string Describe()
    {
        var assembly = typeof(HelloWorkUnit).Assembly;
        var plugin = AssemblyLoadContext.GetLoadContext(assembly)?.Name;
        var contract = AssemblyLoadContext.GetLoadContext(typeof(IWorkUnit).Assembly)?.Name;
        return $"{Greeting}; started={_startedWith}; ranAs='{_ranAs}'; plugin context='{plugin}'; contract context='{contract}'; location='{assembly.Location}'";
    }
}
