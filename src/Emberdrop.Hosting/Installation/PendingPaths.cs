using System.Threading.Channels;

namespace Emberdrop.Plugins;

/// <summary>
/// A first-in first-out list of file paths in which a path that is already waiting is not added again. One file copied into a folder raises
/// several change events; this turns them into one unit of work.
/// </summary>
public sealed class PendingPaths
{
    private readonly object _gate = new();
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Adds the path unless it is already waiting. Returns whether it was added.</summary>
    public bool Enqueue(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (_gate)
        {
            return _waiting.Add(path) && _channel.Writer.TryWrite(path);
        }
    }

    /// <summary>Reads paths in order. A path is released for queueing again the moment it is taken, so a change that happens while it is being processed is not lost.</summary>
    public async IAsyncEnumerable<string> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var path in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            lock (_gate)
            {
                _waiting.Remove(path);
            }

            yield return path;
        }
    }

    /// <summary>Stops accepting paths; readers finish once the queue is empty.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
