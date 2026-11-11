namespace Emberdrop.Plugins;

/// <summary>
/// Says which versions of a plugin a stored work unit may run on. Written as text in the work unit definition.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>*</c> or empty: the latest loaded version (highest Semantic Versioning precedence).</description></item>
/// <item><description><c>1.2.3</c>: exactly that version. This is how a work unit is pinned.</description></item>
/// <item><description><c>^1</c>, <c>^1.2</c>, <c>^1.2.3</c>: the latest loaded version that is at least that and has the same major version.</description></item>
/// </list>
/// </remarks>
public sealed class PluginVersionConstraint : IEquatable<PluginVersionConstraint>
{
    private readonly PluginVersion? _version;

    private PluginVersionConstraint(PluginVersionConstraintKind kind, PluginVersion? version)
    {
        Kind = kind;
        _version = version;
    }

    /// <summary>The latest loaded version, whatever it is.</summary>
    public static PluginVersionConstraint Latest { get; } = new(PluginVersionConstraintKind.Latest, null);

    /// <summary>How versions are selected.</summary>
    public PluginVersionConstraintKind Kind { get; }

    /// <summary>Exactly <paramref name="version"/>.</summary>
    public static PluginVersionConstraint Exactly(PluginVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new(PluginVersionConstraintKind.Exact, version);
    }

    /// <summary>At least <paramref name="minimum"/> and with the same major version.</summary>
    public static PluginVersionConstraint SameMajorAtLeast(PluginVersion minimum)
    {
        ArgumentNullException.ThrowIfNull(minimum);
        return new(PluginVersionConstraintKind.SameMajor, minimum);
    }

    /// <summary>Parses a constraint, throwing <see cref="FormatException"/> if it is not valid.</summary>
    public static PluginVersionConstraint Parse(string? text) =>
        TryParse(text, out var constraint) ? constraint : throw new FormatException($"'{text}' is not a valid version constraint (use '*', '1.2.3' or '^1.2').");

    /// <summary>Parses a constraint, returning <see langword="false"/> if it is not valid. Null and empty mean <see cref="Latest"/>.</summary>
    public static bool TryParse(string? text, out PluginVersionConstraint constraint)
    {
        constraint = Latest;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "*")
        {
            return true;
        }

        if (trimmed[0] == '^')
        {
            var parts = trimmed[1..].Split('.');
            if (parts.Length is < 1 or > 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            {
                return false;
            }

            var padded = string.Join('.', parts.Concat(Enumerable.Repeat("0", 3 - parts.Length)));
            if (!PluginVersion.TryParse(padded, out var minimum))
            {
                return false;
            }

            constraint = SameMajorAtLeast(minimum);
            return true;
        }

        if (PluginVersion.TryParse(trimmed, out var exact))
        {
            constraint = Exactly(exact);
            return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="version"/> is allowed by this constraint.</summary>
    public bool IsSatisfiedBy(PluginVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return Kind switch
        {
            PluginVersionConstraintKind.Latest => true,
            PluginVersionConstraintKind.Exact => version == _version,
            _ => version.Major == _version!.Major && version >= _version,
        };
    }

    /// <inheritdoc />
    public bool Equals(PluginVersionConstraint? other) => other is not null && Kind == other.Kind && _version == other._version;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginVersionConstraint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, _version);

    /// <summary>The canonical text form, which parses back to an equal constraint.</summary>
    public override string ToString() => Kind switch
    {
        PluginVersionConstraintKind.Latest => "*",
        PluginVersionConstraintKind.Exact => _version!.ToString(),
        _ => "^" + _version,
    };
}

/// <summary>How a <see cref="PluginVersionConstraint"/> selects versions.</summary>
public enum PluginVersionConstraintKind
{
    /// <summary>The latest loaded version.</summary>
    Latest = 0,

    /// <summary>Exactly one version.</summary>
    Exact = 1,

    /// <summary>The latest version with the same major version, at least the given one.</summary>
    SameMajor = 2,
}
