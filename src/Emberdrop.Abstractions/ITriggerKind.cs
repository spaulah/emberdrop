using System.Text.Json;

namespace Emberdrop;

/// <summary>
/// Describes one kind of trigger to the host: how to read it from a manifest or an API request and how to read it back from
/// its text token. Implement it and add it to <see cref="TriggerKindRegistry"/> to teach the host a new kind of trigger.
/// </summary>
public interface ITriggerKind
{
    /// <summary>The kind name, as it appears in a manifest (<c>{ "kind": "recurring", ... }</c>) and as the prefix of a token.</summary>
    string Kind { get; }

    /// <summary>Reads a trigger from its JSON object (the <c>kind</c> property is already known to match).</summary>
    /// <exception cref="FormatException">The object is not a valid trigger of this kind; the message says what is wrong.</exception>
    Trigger FromJson(JsonElement element);

    /// <summary>Reads a trigger from the part of its token after <c>kind:</c> (empty when the token is the bare kind).</summary>
    /// <exception cref="FormatException">The detail is not valid for this kind.</exception>
    Trigger FromToken(string detail);
}

internal sealed class ManualTriggerKind : ITriggerKind
{
    public string Kind => ManualTrigger.KindName;

    public Trigger FromJson(JsonElement element) => ManualTrigger.Instance;

    public Trigger FromToken(string detail) => ManualTrigger.Instance;
}

internal sealed class OneOffTriggerKind : ITriggerKind
{
    public string Kind => OneOffTrigger.KindName;

    public Trigger FromJson(JsonElement element)
    {
        if (!element.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("a one-off trigger needs an 'at' property with an ISO 8601 date and time.");
        }

        return FromToken(at.GetString()!);
    }

    public Trigger FromToken(string detail) =>
        DateTimeOffset.TryParse(detail, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
            ? new OneOffTrigger(at)
            : throw new FormatException($"'{detail}' is not an ISO 8601 date and time.");
}

internal sealed class RecurringTriggerKind : ITriggerKind
{
    public string Kind => RecurringTrigger.KindName;

    public Trigger FromJson(JsonElement element)
    {
        if (!element.TryGetProperty("cron", out var cron) || cron.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("a recurring trigger needs a 'cron' property with a cron expression.");
        }

        return FromToken(cron.GetString()!);
    }

    public Trigger FromToken(string detail)
    {
        var fields = detail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length is 5 or 6
            ? new RecurringTrigger(detail)
            : throw new FormatException($"cron expression '{detail}' must have 5 or 6 fields.");
    }
}
