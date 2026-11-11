using Emberdrop;
using Fixture.Conflict;
using Fixture.Contracts;

namespace Fixture.UsesConflict;

/// <summary>A plugin that depends on version 2 of a library the host only has in version 1.</summary>
public sealed class UsesConflictWorkUnit : IWorkUnit, IProbe
{
    public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken) => Task.FromResult(WorkResult.Succeeded());

    public string Describe() => "plugin sees " + ConflictLibrary.Describe();
}
