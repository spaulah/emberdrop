# ADR 0009: Triggers, typed input and output, and the manifest

- Status: accepted
- Date: 2026-11-08

## Context

[ADR 0008](0008-work-units-not-jobs.md) made the work unit the core idea. A work unit still had one hard-wired way to start (a cron string on the stored row), took its input as an opaque string, and its manifest entry said little about it.

## Decision

**Triggers are polymorphic and the host reads them through a registry.** `Trigger` has three implementations in v0.1 (manual, one-off, recurring). `ITriggerKind` plus `TriggerKindRegistry` is the extension point: it tells the host how to read a kind from a manifest or API object and from its text token. Registering a kind lets the host read and carry it; making something fire it is a scheduler's job. We implement no kind beyond the three.

**Manifest triggers are defaults; operator triggers are stored apart.** A plugin's triggers belong to the plugin version and are never copied into the operator's data. The first recurring one feeds the existing default-schedule mechanism ([ADR 0006](0006-schedule-reconciliation.md)). Triggers an operator adds live in their own table, so a plugin update cannot reach them, and the reconciliation rules stay as they were. v0.1 stores recurring operator triggers only; manual and one-off runs are requested per execution and recorded with their trigger.

**Executions record their trigger.** The trigger travels in the invocation to the scheduler and back into `WorkContext.Trigger`, as a plain-text token so a scheduler engine stores only strings.

**Types come from the class; the manifest may confirm them.** The host reads `TInput` and `TOutput` from the `WorkUnit<TInput, TOutput>` base class, generates JSON Schemas with `System.Text.Json`'s schema exporter, stores them with the work unit and returns them from the API. `inputType` and `outputType` in the manifest are optional and must match the class, so a manifest cannot lie about it.

**Input is validated by strict reading, not by schema.** Input is deserialized into `TInput` with unknown properties disallowed, before the work unit starts, in the host (so a bad input is refused whatever the work unit does) and by the API before it accepts input. We did not add a JSON Schema validation library; this is the cheapest check that cannot disagree with what the work unit will actually read. Constraints a schema can express and a type cannot (ranges, patterns) are not checked.

**Capabilities are declared, validated, stored and shown, and that is all.** Only `service:` capabilities are checked, against the host's service allow-list. We say so in the documentation rather than imply a sandbox.

**Kind is a string.** The host runs `work-unit` and rejects any other kind until its owner adds it, so a manifest can never silently ask for something the host will not do.

## Consequences

- Serializer options are created per call and never shared (`WorkJson.CreateOptions`), and the host clears `System.Text.Json`'s runtime caches while unloading a plugin version. Without both, reading, writing or describing a plugin's types pins its load context. The clearing uses the internal handler the runtime itself calls on hot reload; if a runtime drops it, unloads of plugins that used typed input will report "not freed" instead of silently leaking. This is recorded in the known limits.
- A recurring operator trigger and the work unit's own schedule are separate scheduler entries, so an operator can have several.
- A one-off run does not survive a restart on a scheduler that keeps nothing on disk (the in-memory one); that is the scheduler's property, not the model's.
- `defaultSchedule` is gone from the manifest in favour of `triggers`.

## Alternatives considered

**Store manifest triggers in the database with a flag saying who owns them.** Needs reconciliation rules for every field of every trigger; keeping the plugin's triggers out of the operator's data avoids the problem.

**A JSON Schema validation dependency.** Listed as an open question instead of added here.

**One `Schedule` column that grows more cases.** It cannot say "manual" or "one-off" and cannot hold several.
