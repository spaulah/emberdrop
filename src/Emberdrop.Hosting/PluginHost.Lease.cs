namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;

using Microsoft.Extensions.DependencyInjection;

public sealed partial class PluginHost
{
    private sealed class ExportLease(PluginHost host, PluginVersionRecord record, ExecutionRecord execution, ExportDescriptor export) : IDisposable
    {
        private int _disposed;

        public CancellationToken Token => execution.Cancellation.Token;

        public async Task<TResult> RunAsync<TResult>(Func<ExportInvocation, Task<TResult>> run, Action<Type>? preflight)
        {
            LoadedPlugin plugin;
            lock (host._gate)
            {
                // The lease keeps the version from unloading, so the plugin is still here.
                plugin = record.Plugin!;
            }

            // Refused before any plugin code runs, and not wrapped: the host's own check says what is wrong.
            if (preflight is not null)
            {
                plugin.Inspect(export.Name, type =>
                {
                    preflight(type);
                    return 0;
                });
            }

            IServiceScope? scope = null;
            object? instance = null;
            var abandoned = false;
            try
            {
                Task<TResult> work;
                try
                {
                    // Every execution gets its own scope of the plugin's own provider.
                    scope = plugin.GetServices(host._services).CreateScope();
                    instance = plugin.CreateExport(export.Name, scope.ServiceProvider);
                    work = run(new ExportInvocation(instance, scope.ServiceProvider, plugin.Descriptor, export, execution.Id, record.Activities, Token));
                }
                catch (Exception ex)
                {
                    throw Translate(ex);
                }

                var finished = await Task.WhenAny(work, execution.AbandonSignal.Task).ConfigureAwait(false);
                if (finished != work)
                {
                    abandoned = true;
                    // Keep the late result from going unobserved; the plugin's code is still running.
                    _ = work.ContinueWith(
                        t =>
                        {
                            lock (host._gate)
                            {
                                execution.StillRunning = false;
                            }

                            _ = t.Exception;
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw new PluginExecutionAbandonedException(
                        $"The execution of '{execution.ExportName}' on {record.Label} was abandoned because the plugin version had to be retired while it was still running.");
                }

                try
                {
                    return await work.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw Translate(ex);
                }
            }
            finally
            {
                // An abandoned run's code may still be using its instance and scope; they are disposed with the plugin's provider instead.
                if (!abandoned)
                {
                    await DisposeQuietlyAsync(instance, scope).ConfigureAwait(false);
                }
            }
        }

        private async Task DisposeQuietlyAsync(object? instance, IServiceScope? scope)
        {
            try
            {
                if (instance is IAsyncDisposable asyncInstance)
                {
                    await asyncInstance.DisposeAsync().ConfigureAwait(false);
                }
                else if (instance is IDisposable syncInstance)
                {
                    syncInstance.Dispose();
                }

                if (scope is IAsyncDisposable asyncScope)
                {
                    await asyncScope.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    scope?.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Text only; never keep an exception object that plugin code created.
                host._logger.LogWarning("Disposing an export of {Plugin} failed: {Type}: {Message}", record.Label, ex.GetType().Name, ex.Message);
            }
        }

        /// <summary>Replaces an exception from plugin code with a host-owned one that holds text only (see <see cref="PluginExecutionException"/>).</summary>
        private Exception Translate(Exception ex)
        {
            if (ex is OperationCanceledException && Token.IsCancellationRequested)
            {
                return new OperationCanceledException($"The execution of '{execution.ExportName}' on {record.Label} was cancelled.", Token);
            }

            return new PluginExecutionException(
                $"Export '{execution.ExportName}' on {record.Label} failed: {ex.GetType().Name}: {ex.Message}",
                ex.ToString());
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                host.EndExecution(record, execution);
            }
        }
    }

}
