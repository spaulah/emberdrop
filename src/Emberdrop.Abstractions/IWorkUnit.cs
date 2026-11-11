namespace Emberdrop;

/// <summary>
/// A unit of executable code that a plugin provides and a host can discover, bind to a plugin version, trigger, drain and unload.
/// Plugin packages implement this interface (usually through <see cref="WorkUnit{TInput, TOutput}"/>) in their own assemblies.
/// </summary>
public interface IWorkUnit
{
    /// <summary>Runs the work unit once.</summary>
    /// <param name="context">What is being run, on whose behalf, and what caused it.</param>
    /// <param name="cancellationToken">
    /// Signalled when the host wants the execution to stop, for example because the plugin version it belongs to is being
    /// replaced and has waited long enough. Work units must honor it promptly: code that ignores it cannot be stopped and
    /// keeps its plugin version loaded.
    /// </param>
    /// <returns>
    /// The outcome. Returning <see cref="WorkResult.Failed"/> reports an expected failure; an unhandled exception is also a failure.
    /// </returns>
    Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken);
}
