namespace Emberdrop.Plugins;

/// <summary>How waiting for a file to be complete ended.</summary>
public enum StableFileResult
{
    /// <summary>The file is complete: its size and modification time held still and it could be opened exclusively.</summary>
    Stable = 0,

    /// <summary>The file disappeared while waiting.</summary>
    Gone = 1,

    /// <summary>The file kept changing or stayed locked until the timeout.</summary>
    TimedOut = 2,
}

/// <summary>
/// Decides when a file that just appeared has been completely written. A file event says a file <i>started</i> to exist, not
/// that its writer is done, and a large copy over a network share can take minutes.
/// </summary>
public static class StableFileDetector
{
    /// <summary>
    /// Waits until the file's size and modification time are identical across <paramref name="checks"/> consecutive looks and the
    /// file can be opened with no sharing, which fails while any writer still holds it.
    /// </summary>
    public static async Task<StableFileResult> WaitAsync(string path, TimeSpan interval, int checks, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var deadline = DateTime.UtcNow + timeout;
        (long Length, DateTime Written)? last = null;
        var unchanged = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return StableFileResult.Gone;
            }

            var state = (info.Length, info.LastWriteTimeUtc);
            if (last == state)
            {
                unchanged++;
            }
            else
            {
                last = state;
                unchanged = 0;
            }

            if (unchanged >= checks && CanOpenExclusively(path))
            {
                return StableFileResult.Stable;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return StableFileResult.TimedOut;
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
