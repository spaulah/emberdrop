using System.Globalization;

namespace Emberdrop.Plugins;

/// <summary>
/// A plugin version in Semantic Versioning 2.0 form: <c>major.minor.patch</c> with an optional
/// <c>-prerelease</c> and an optional <c>+build</c> suffix. Build metadata is kept for display but ignored in comparisons.
/// </summary>
public sealed class PluginVersion : IComparable<PluginVersion>, IEquatable<PluginVersion>
{
    private readonly string[] _prerelease;

    private PluginVersion(int major, int minor, int patch, string prerelease, string build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
        _prerelease = prerelease.Length == 0 ? [] : prerelease.Split('.');
    }

    /// <summary>The major version. Plugins that share a major version are expected to be compatible.</summary>
    public int Major { get; }

    /// <summary>The minor version.</summary>
    public int Minor { get; }

    /// <summary>The patch version.</summary>
    public int Patch { get; }

    /// <summary>The pre-release label without the leading dash, or an empty string.</summary>
    public string Prerelease { get; }

    /// <summary>The build metadata without the leading plus, or an empty string.</summary>
    public string Build { get; }

    /// <summary>Whether this is a pre-release version.</summary>
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Parses a version, throwing <see cref="FormatException"/> if it is not valid.</summary>
    public static PluginVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a valid version (expected major.minor.patch[-prerelease][+build]).");

    /// <summary>Parses a version, returning <see langword="false"/> if it is not valid.</summary>
    public static bool TryParse(string? text, out PluginVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var rest = text.Trim();
        var build = string.Empty;
        var plus = rest.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            build = rest[(plus + 1)..];
            rest = rest[..plus];
            if (!AreValidIdentifiers(build, allowLeadingZeros: true))
            {
                return false;
            }
        }

        var prerelease = string.Empty;
        var dash = rest.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = rest[(dash + 1)..];
            rest = rest[..dash];
            if (!AreValidIdentifiers(prerelease, allowLeadingZeros: false))
            {
                return false;
            }
        }

        var parts = rest.Split('.');
        if (parts.Length != 3 || !TryParseNumber(parts[0], out var major) || !TryParseNumber(parts[1], out var minor) || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new PluginVersion(major, minor, patch, prerelease, build);
        return true;
    }

    /// <summary>Orders versions by Semantic Versioning precedence. A pre-release sorts before its release.</summary>
    public int CompareTo(PluginVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        return ComparePrerelease(other);
    }

    /// <inheritdoc />
    public bool Equals(PluginVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}") + (IsPrerelease ? "-" + Prerelease : string.Empty) + (Build.Length > 0 ? "+" + Build : string.Empty);

    /// <summary>Compares two versions for equality by precedence.</summary>
    public static bool operator ==(PluginVersion? left, PluginVersion? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Compares two versions for inequality by precedence.</summary>
    public static bool operator !=(PluginVersion? left, PluginVersion? right) => !(left == right);

    /// <summary>Returns whether <paramref name="left"/> has lower precedence than <paramref name="right"/>.</summary>
    public static bool operator <(PluginVersion? left, PluginVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Returns whether <paramref name="left"/> has higher precedence than <paramref name="right"/>.</summary>
    public static bool operator >(PluginVersion? left, PluginVersion? right) => left is not null && left.CompareTo(right) > 0;

    /// <summary>Returns whether <paramref name="left"/> has lower or equal precedence compared to <paramref name="right"/>.</summary>
    public static bool operator <=(PluginVersion? left, PluginVersion? right) => !(left > right);

    /// <summary>Returns whether <paramref name="left"/> has higher or equal precedence compared to <paramref name="right"/>.</summary>
    public static bool operator >=(PluginVersion? left, PluginVersion? right) => !(left < right);

    private int ComparePrerelease(PluginVersion other)
    {
        if (_prerelease.Length == 0 || other._prerelease.Length == 0)
        {
            // A release has higher precedence than any of its pre-releases.
            return other._prerelease.Length.CompareTo(_prerelease.Length);
        }

        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            int result;
            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric != rightNumeric)
            {
                // Numeric identifiers sort before alphanumeric ones.
                result = leftNumeric ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(left, right);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0') || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool AreValidIdentifiers(string text, bool allowLeadingZeros)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var identifier in text.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }

            if (!allowLeadingZeros && identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }
}
