using System.Runtime.CompilerServices;
using Emberdrop;
using Fixture.Contracts;

namespace Fixture.Fragile;

internal static class Build
{
#if V2
#pragma warning disable CA2255 // A module initializer is exactly what this fixture exists to exercise.
    public const string Version = "v2";
#else
    public const string Version = "v1";
#endif
}

public sealed class OkWorkUnit : IWorkUnit
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        FixtureControl.Mark($"fragile-ran:{Build.Version}:{context.Input}");
        return Task.FromResult(WorkResult.Succeeded());
    }
}

#if V2
#pragma warning disable CA2255 // A module initializer is exactly what this fixture exists to exercise.
/// <summary>Version 2 of this plugin cannot start: its entry assembly throws as soon as it is initialized.</summary>
internal static class Explodes
{
    [ModuleInitializer]
    internal static void Initialize() => throw new InvalidOperationException("entry assembly exploded on load");
}
#endif
