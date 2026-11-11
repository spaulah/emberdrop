using System.Text.Json;

namespace Emberdrop;

/// <summary>
/// Convenience base class for work units with typed input and output: it reads <see cref="WorkContext.Input"/> into
/// <typeparamref name="TInput"/>, calls <see cref="ExecuteAsync(TInput, WorkContext, CancellationToken)"/>, and serializes the result.
/// Use <see cref="NoInput"/> or <see cref="NoOutput"/> when there is none.
/// </summary>
/// <typeparam name="TInput">The input type; read strictly from JSON (see <see cref="WorkJson.CreateOptions()"/>).</typeparam>
/// <typeparam name="TOutput">The output type; written as JSON.</typeparam>
public abstract class WorkUnit<TInput, TOutput> : IWorkUnit
{
    /// <inheritdoc />
    /// <exception cref="WorkInputException">The input is missing or does not fit <typeparamref name="TInput"/>.</exception>
    public async Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var input = ReadInput(context);
        var output = await ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
        return WorkResult.Succeeded(output);
    }

    /// <summary>Runs the work unit with its typed input.</summary>
    /// <param name="input">The input; <see cref="NoInput.Instance"/> when <typeparamref name="TInput"/> is <see cref="NoInput"/>.</param>
    /// <param name="context">What is being run and on whose behalf.</param>
    /// <param name="cancellationToken">Signalled when the host wants the execution to stop; honor it promptly.</param>
    protected abstract Task<TOutput> ExecuteAsync(TInput input, WorkContext context, CancellationToken cancellationToken);

    private static TInput ReadInput(WorkContext context)
    {
        if (typeof(TInput) == typeof(NoInput))
        {
            return (TInput)(object)NoInput.Instance;
        }

        if (string.IsNullOrWhiteSpace(context.Input))
        {
            throw new WorkInputException($"Work unit '{context.WorkUnitName}' requires input of type {typeof(TInput).Name}, but none was given.");
        }

        try
        {
            return JsonSerializer.Deserialize<TInput>(context.Input, WorkJson.CreateOptions())
                ?? throw new WorkInputException($"The input of work unit '{context.WorkUnitName}' is JSON null; it requires {typeof(TInput).Name}.");
        }
        catch (JsonException ex)
        {
            throw new WorkInputException($"The input of work unit '{context.WorkUnitName}' is not a valid {typeof(TInput).Name}: {ex.Message}", ex);
        }
    }
}
