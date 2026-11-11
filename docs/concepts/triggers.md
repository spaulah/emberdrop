# Triggers

A trigger is what causes an execution. v0.1 has exactly three, and they are polymorphic (`Trigger` and its subtypes), so more can be added.

| Kind | Type | Meaning | Where it can come from |
| --- | --- | --- | --- |
| `manual` | `ManualTrigger` | On demand. | Declared in a manifest (documentation: "this can be started by hand"); recorded for every `POST /api/work-units/{id}/executions` without `runAt`. |
| `one-off` | `OneOffTrigger(At)` | Once, at a time (delayed). | Recorded for `POST .../executions` with `runAt`. It is a single point in time, so a manifest cannot declare it as a default. |
| `recurring` | `RecurringTrigger(Cron)` | On a cron schedule. | Declared in a manifest (a default); added by an operator (`POST /api/work-units/{id}/triggers`). |

## Defaults and operator triggers

- **Declared by the plugin.** `triggers` in the manifest are defaults. They travel with the plugin version and are shown as `declaredTriggers` on the work unit. The first recurring one becomes the work unit's default schedule, with the rules in [ADR 0006](../adr/0006-schedule-reconciliation.md): an operator's schedule override always wins and survives updates.
- **Added by an operator.** `POST /api/work-units/{id}/triggers` (body `{ "kind": "recurring", "cron": "0 9 * * *" }`) stores a trigger in its own table, `WorkUnitTrigger`, apart from the work unit's definition. A plugin update never touches it, and it is scheduled beside the work unit's own entry (key `<identifier>:trigger:<id>`). It is paused with the work unit when the plugin cannot run, and deleted with it. v0.1 stores recurring triggers only; a manual or one-off run is requested per execution.
- **Recorded with the execution.** Every execution carries the trigger that caused it: schedulers keep it in the invocation (`WorkInvocation.Trigger`), `WorkContext.Trigger` hands it to the work unit, and `GET /api/scheduler/executions` shows it.

## Adding a kind of trigger

A trigger kind has two parts: a record that derives from `Trigger`, and an `ITriggerKind` that tells the host how to read it.

```csharp
public sealed record QueueTrigger(string Queue) : Trigger
{
    public override string Kind => "queue";
    public override string ToToken() => "queue:" + Queue;      // the plain-text form schedulers and stores keep
}

public sealed class QueueTriggerKind : ITriggerKind
{
    public string Kind => "queue";
    public Trigger FromJson(JsonElement element) => new QueueTrigger(element.GetProperty("queue").GetString()!);  // a manifest or API object
    public Trigger FromToken(string detail) => new QueueTrigger(detail);                                           // the text after "queue:"
}
```

Register it where you set Emberdrop up:

```csharp
services.AddEmberdrop(builder => builder
    .UseStore(...)
    .UseScheduler(...)
    .AddTriggerKind(new QueueTriggerKind()));
```

From then on the host reads `{ "kind": "queue", "queue": "orders" }` in manifests, and tokens like `queue:orders` in scheduler history. `FromJson` and `FromToken` should throw `FormatException` with a message fit to show a user.

**What registering does not do.** The host *reads and carries* the new kind. Making something *fire* it is the job of a scheduler provider or of your own code: the only kinds the bundled schedulers start are manual (enqueue), one-off (delayed) and recurring (cron). A stored operator trigger is scheduled by the host only if it is recurring. This is the extension seam; v0.1 implements no other kind.
