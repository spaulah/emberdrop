using System.Text.Json;

namespace Emberdrop;

/// <summary>
/// The trigger kinds a host understands. A new registry knows the three built-in kinds (manual, one-off, recurring);
/// <see cref="Add"/> registers more.
/// </summary>
public sealed class TriggerKindRegistry
{
    private readonly Dictionary<string, ITriggerKind> _kinds = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates a registry with the built-in kinds.</summary>
    public TriggerKindRegistry()
    {
        Add(new ManualTriggerKind());
        Add(new OneOffTriggerKind());
        Add(new RecurringTriggerKind());
    }

    /// <summary>The names of the registered kinds.</summary>
    public IReadOnlyList<string> Kinds
    {
        get
        {
            lock (_gate)
            {
                return _kinds.Keys.Order(StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>Registers a kind.</summary>
    /// <exception cref="InvalidOperationException">A kind with that name is already registered.</exception>
    public void Add(ITriggerKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Kind);

        lock (_gate)
        {
            if (!_kinds.TryAdd(kind.Kind, kind))
            {
                throw new InvalidOperationException($"A trigger kind named '{kind.Kind}' is already registered.");
            }
        }
    }

    /// <summary>Reads a trigger from a JSON object with a <c>kind</c> property.</summary>
    /// <exception cref="FormatException">The kind is missing or unknown, or the object is invalid for it. The message is fit to show to a user.</exception>
    public Trigger FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("a trigger must be an object with a 'kind'.");
        }

        var kind = kindElement.GetString()!;
        return Find(kind).FromJson(element);
    }

    /// <summary>Reads a trigger from its token (see <see cref="Trigger.ToToken"/>). <see langword="null"/> or empty is a <see cref="ManualTrigger"/>.</summary>
    /// <exception cref="FormatException">The token is not a known trigger.</exception>
    public Trigger Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ManualTrigger.Instance;
        }

        var separator = token.IndexOf(':', StringComparison.Ordinal);
        var kind = separator < 0 ? token : token[..separator];
        var detail = separator < 0 ? string.Empty : token[(separator + 1)..];
        return Find(kind).FromToken(detail);
    }

    private ITriggerKind Find(string kind)
    {
        lock (_gate)
        {
            return _kinds.TryGetValue(kind, out var found)
                ? found
                : throw new FormatException($"'{kind}' is not a known trigger kind (known: {string.Join(", ", _kinds.Keys.Order(StringComparer.Ordinal))}).");
        }
    }
}
