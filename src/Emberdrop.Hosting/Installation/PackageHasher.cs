using System.Security.Cryptography;

namespace Emberdrop.Plugins;

/// <summary>Computes the SHA-256 of a package file.</summary>
public static class PackageHasher
{
    /// <summary>Returns the lowercase hexadecimal SHA-256 of the file.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Whether <paramref name="hash"/> is allowed. An empty allowlist allows everything: the check is off.</summary>
    public static bool IsAllowed(string hash, IEnumerable<string> allowlist)
    {
        var allowed = allowlist.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        return allowed.Count == 0 || allowed.Any(h => string.Equals(h.Trim(), hash, StringComparison.OrdinalIgnoreCase));
    }
}
