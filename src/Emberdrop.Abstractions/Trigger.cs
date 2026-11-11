namespace Emberdrop;

/// <summary>
/// What causes an execution. Triggers are polymorphic: v0.1 has exactly <see cref="ManualTrigger"/>, <see cref="OneOffTrigger"/>
/// and <see cref="RecurringTrigger"/>. A host can add a kind by implementing <see cref="ITriggerKind"/> and registering it
/// (see <c>docs/concepts/triggers.md</c>).
/// </summary>
public abstract record Trigger
{
    /// <summary>A short stable name for the kind of trigger (<c>manual</c>, <c>one-off</c>, <c>recurring</c>).</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// A compact text form (<c>kind</c> or <c>kind:detail</c>) that schedulers and stores can keep as a plain string;
    /// <see cref="TriggerKindRegistry.Parse"/> reverses it.
    /// </summary>
    public abstract string ToToken();
}

/// <summary>An execution someone asked for on demand.</summary>
public sealed record ManualTrigger : Trigger
{
    /// <summary>The kind name.</summary>
    public const string KindName = "manual";

    /// <summary>The only value.</summary>
    public static ManualTrigger Instance { get; } = new();

    /// <inheritdoc />
    public override string Kind => KindName;

    /// <inheritdoc />
    public override string ToToken() => KindName;
}

/// <summary>An execution that happens once, at a given time (delayed).</summary>
/// <param name="At">When the execution was scheduled for.</param>
public sealed record OneOffTrigger(DateTimeOffset At) : Trigger
{
    /// <summary>The kind name.</summary>
    public const string KindName = "one-off";

    /// <inheritdoc />
    public override string Kind => KindName;

    /// <inheritdoc />
    public override string ToToken() => $"{KindName}:{At.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>An execution that repeats on a cron schedule.</summary>
/// <param name="Cron">The five- or six-field cron expression.</param>
public sealed record RecurringTrigger(string Cron) : Trigger
{
    /// <summary>The kind name.</summary>
    public const string KindName = "recurring";

    /// <inheritdoc />
    public override string Kind => KindName;

    /// <inheritdoc />
    public override string ToToken() => $"{KindName}:{Cron}";
}
