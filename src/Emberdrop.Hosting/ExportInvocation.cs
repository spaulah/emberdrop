namespace Emberdrop.Plugins;

/// <summary>What <see cref="PluginHost.ExecuteExportAsync{TResult}"/> hands to the code it runs.</summary>
/// <param name="Instance">A fresh instance of the export, created from the plugin's own service scope.</param>
/// <param name="Services">The service scope of this execution: a scope of the plugin version's own provider.</param>
/// <param name="Plugin">The plugin version the execution is bound to.</param>
/// <param name="Export">The export being run.</param>
/// <param name="ExecutionId">The id of this execution as the hosting core tracks it.</param>
/// <param name="Activity">The hook through which code can tell the host about long-lived things it starts.</param>
/// <param name="CancellationToken">Signalled when the host wants the execution to stop, for example because the version is being retired.</param>
public sealed record ExportInvocation(
    object Instance,
    IServiceProvider Services,
    PluginDescriptor Plugin,
    ExportDescriptor Export,
    string ExecutionId,
    IPluginActivity Activity,
    CancellationToken CancellationToken);

/// <summary>A choice of one export of one plugin version, or the reason there is none.</summary>
/// <param name="Plugin">The chosen plugin version.</param>
/// <param name="Export">The chosen export.</param>
/// <param name="Reason">When nothing was chosen: why, in words an operator can act on.</param>
public readonly record struct ExportSelection(PluginDescriptor? Plugin, ExportDescriptor? Export, string? Reason)
{
    /// <summary>Chooses this export of this plugin version.</summary>
    public static ExportSelection Found(PluginDescriptor plugin, ExportDescriptor export) => new(plugin, export, null);

    /// <summary>Chooses nothing, for the stated reason.</summary>
    public static ExportSelection NotFound(string reason) => new(null, null, reason);
}

/// <summary>Whether <see cref="PluginHost.ExecuteExportAsync{TResult}"/> ran the code, and what it returned.</summary>
/// <typeparam name="TResult">What the run returns.</typeparam>
/// <param name="Executed"><see langword="true"/> if the code ran to completion.</param>
/// <param name="NotExecutedReason">When nothing ran: why no loaded plugin version could provide the export.</param>
/// <param name="Result">When it ran: what it returned.</param>
public readonly record struct ExportExecution<TResult>(bool Executed, string? NotExecutedReason, TResult? Result = default)
{
}

/// <summary>Creates <see cref="ExportExecution{TResult}"/> values.</summary>
public static class ExportExecution
{
    /// <summary>The code ran and returned <paramref name="result"/>.</summary>
    public static ExportExecution<TResult> Completed<TResult>(TResult result) => new(true, null, result);

    /// <summary>Nothing ran, for the stated reason.</summary>
    public static ExportExecution<TResult> NotExecuted<TResult>(string reason) => new(false, reason);
}
