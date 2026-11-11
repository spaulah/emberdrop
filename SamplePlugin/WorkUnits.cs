using System.Runtime.Loader;
using Emberdrop;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace SamplePlugin;

/// <summary>A recurring work unit with no input: it runs on the schedule its manifest declares.</summary>
public class HeartbeatWorkUnit : WorkUnit<NoInput, NoOutput>
{
    protected override async Task<NoOutput> ExecuteAsync(NoInput input, WorkContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation(
            "Heartbeat from execution {ExecutionId} of {WorkUnit} {PluginVersion}, caused by a {Trigger} trigger.",
            context.ExecutionId, context.WorkUnitName, context.PluginVersion, context.Trigger.Kind);
        await Task.Delay(500, cancellationToken);
        return NoOutput.Instance;
    }
}

/// <summary>What the <c>greet</c> work unit needs to be started.</summary>
/// <param name="Name">Who to greet.</param>
public sealed record GreetInput(string Name);

/// <summary>What the <c>greet</c> work unit returns.</summary>
/// <param name="Message">The greeting.</param>
/// <param name="HumanizerLoadedInto">The load context Humanizer, this plugin's own dependency, was loaded into.</param>
public sealed record GreetOutput(string Message, string? HumanizerLoadedInto);

/// <summary>
/// A manual work unit with typed input and output. Start it with
/// <c>POST /api/work-units/{id}/executions</c> and a body like <c>{ "input": { "name": "ada lovelace" } }</c>.
/// </summary>
public class GreetWorkUnit : WorkUnit<GreetInput, GreetOutput>
{
    protected override Task<GreetOutput> ExecuteAsync(GreetInput input, WorkContext context, CancellationToken cancellationToken)
    {
        // Humanizer is this plugin's own dependency (see SamplePlugin.csproj). It runs from the plugin's private load context.
        var loadedInto = AssemblyLoadContext.GetLoadContext(typeof(StringHumanizeExtensions).Assembly)?.Name;
        var message = $"Hello, {input.Name.Titleize()}! This is {context.WorkUnitName} {context.PluginVersion}.";
        context.Logger.LogInformation("Execution {ExecutionId}: {Message}", context.ExecutionId, message);
        return Task.FromResult(new GreetOutput(message, loadedInto));
    }
}
