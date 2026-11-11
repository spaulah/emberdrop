# ADR 0005: Stored work units bind to a plugin, a version constraint and a logical name

- Status: accepted
- Date: 2026-11-07

## Context

A stored work unit must keep working across plugin updates, and must keep working when its plugin is temporarily absent. The first design stored the work unit's class name (`SampleBackgroundWorkUnit`) and found it by scanning the loaded plugins. That breaks in four ways: two plugins can have a class with the same name; renaming a class breaks every stored work unit; there is no way to say "stay on version 1" or "go to the next major only when I say"; and a missing plugin turns into a permanent failure.

## Decision

A stored work unit carries:

| Field | Meaning |
|---|---|
| `PluginId` | Which plugin provides it. `null` for a work unit built into the host. |
| `WorkUnitName` | The work unit's **logical name** from the plugin's manifest. This, not the CLR type, is the identity; the class can be renamed or moved. |
| `VersionConstraint` | Which versions of the plugin may run it, as text. |
| `InputSchemaVersion` | The schema version of the stored input (`Input`). |

A CLR type name is never stored for a plugin work unit, and an assembly-qualified name never appears anywhere in the store or the scheduler. (The scheduler is handed only a work unit type name and an id; it never sees a plugin type. See [ADR 0001](0001-collectible-load-context-per-plugin-version.md).)

**Version constraints** have three forms:

- `*` or empty: the latest loaded version, by Semantic Versioning precedence.
- `1.2.3`: exactly that version. This is a pin.
- `^1`, `^1.2`, `^1.2.3`: the latest loaded version that is at least that and has the same major version.

A work unit created from a manifest gets `^<major>.0.0` of the version that created it: it follows its plugin through minor and patch updates automatically, and moving to a new major version is the operator's decision.

**Resolution happens when the work unit fires**, against the versions that are `Active` at that moment, and picks the highest-precedence version that satisfies the constraint and provides the work unit. Draining versions never receive new work.

**When nothing matches, the work unit waits.** The run does nothing, the work unit is reported as `WaitingForPlugin` with the reason in words (`No loaded version of plugin 'acme' satisfies ^1.0.0; loaded: 2.0.0.`), its schedule is paused, and the missed run is repeated as soon as a matching version loads. The alternative, failing the run, is available as the `Fail` policy. The status is *computed* from what is loaded, not stored, so it is always accurate and cannot go stale.

**Input carries a schema version.** A manifest work unit declares the schema version of the input it understands (`inputSchemaVersion`, default 1). If the stored work unit's version differs, the run is refused before the work unit starts, with an error that names both versions. Input is never reinterpreted silently, because wrongly parsed input is a data-corruption bug that looks like success. The operator's remedies are explicit: update the input and its schema version, or pin the work unit to a plugin version that expects the stored one.

**Work units stored before bindings existed** (`PluginId` null, a class name as type) are still found by name, and are adopted into the new model the first time a plugin that provides a matching work unit loads. A built-in work unit is never adopted.

## Alternatives rejected

**Keep storing the class name.** See Context.

**Store the assembly-qualified type name** (what Hangfire does when given an object). It binds the work unit to one exact assembly version, so every plugin update orphans its stored work units, and it makes the scheduler hold plugin types.

**Always run the latest version.** Simple, but a breaking change in a plugin's input would take every dependent work unit down the moment it deployed. Constraints give operators the control point.

**Always require an exact version.** Safe and useless: every patch release would need every work unit edited.

**Fail permanently when the plugin is missing.** A plugin that is a few seconds late, mid-update, or deliberately removed for an hour would leave behind failed run history, retry storms and alerts for something that fixes itself.

**Store the status** (`WaitingForPlugin`) in the database. It would need to be kept in step with plugin loads, unloads and crashes, and would be wrong after any of them that was missed.

**Guess when the argument schema differs** (try to parse, fall back). Silent misreads are worse than an explicit failure.

## Consequences

- Renaming a class inside a plugin does not break stored work units; renaming a *work unit* in the manifest does, deliberately: it is a different work unit.
- A repeated run for a waiting work unit is held in memory. After a restart the plugins load first and recurring schedules resume by themselves, so only a one-off missed run can be lost.
- The scheduler records a run that did nothing because its plugin was missing as completed. The truth is in the work unit's status and the logs, not in the engine's run history.
- Prerelease versions are ordered by Semantic Versioning precedence, so `*` can select `2.0.0-beta` over `1.9.0`. Pin or use `^1` to avoid that.
