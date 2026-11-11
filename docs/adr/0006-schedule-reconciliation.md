# ADR 0006: Plugin schedules are defaults; operator choices are never overwritten

- Status: accepted
- Date: 2026-11-07

## Context

A plugin's manifest can suggest a schedule for each work unit. Operators also change schedules, switch work units off, and pin versions. Plugins come, update and go at any time. Without rules, either a plugin update silently throws away operator decisions, or operator edits freeze a work unit so that it never benefits from its plugin's changes again.

## Decision

**Who owns what.**

| Field | Owner | Changed by a plugin update? |
|---|---|---|
| `DefaultSchedule` | the plugin | yes: it follows the newest active version's manifest |
| `ScheduleOverride` | the operator | **never** |
| `Schedule` (the effective schedule) | derived | yes, but only to `ScheduleOverride ?? DefaultSchedule` |
| `Enabled`, `VersionConstraint`, input, `InputSchemaVersion` | the operator | **never** |

So when a new version changes a default schedule, a work unit with an override keeps the override, and a work unit without one gets the new default.

**How an operator edit is recorded.** The REST update sends the whole work unit back. A changed schedule becomes the override; an unchanged one changes nothing (so editing a work unit's name does not freeze its schedule); setting the schedule back to the plugin's default clears the override.

**New work units from a manifest** are registered enabled only if the manifest gives a default schedule and every required configuration key is set. Otherwise they are registered disabled for the operator to switch on. Missing configuration is reported at registration time, in the log and in the work unit's status, before anything can run.

**Pause, never delete.** When the last active version of a plugin stops being active (it is draining, or gone), the schedules of its work units are removed from the scheduler engine and kept in the store. When a matching version is active again they are put back. The same rule pauses a work unit that cannot run for another reason (a required setting is missing, its input does not fit the plugin). One function decides the scheduler's state for a stored work unit, `ScheduleWorkUnitAsync`, so there is one place to get it right.

**Nothing is deleted on update.** A work unit a newer version no longer declares stays in the store, and its status says why it cannot run, until the operator removes it.

**Adoption.** A schedule an operator set by hand on a work unit stored before bindings existed becomes an override when that work unit is adopted, because it is the operator's.

**Serialization.** Plugin changes are reconciled one at a time, so two plugins arriving together cannot create the same stored work unit twice.

## Alternatives rejected

**The plugin's schedule always wins.** Operators could not change a schedule that would survive the next deploy.

**The first edit freezes the schedule** (one `Schedule` column). Simple, and it means a plugin can never improve a default again for any work unit an operator has ever touched, with nothing in the data to say which work units those are.

**Delete a plugin's work units when it is removed.** Loses the operator's configuration, stored input and identity of the work unit; coming back would be a different work unit. Deletion is a decision for a person.

**Pause by switching `Enabled` off.** It would overwrite the operator's flag and make it impossible to tell "the operator switched this off" from "the plugin is away". Pausing lives in the scheduler, which the store does not record.

**Drive reconciliation from a timer.** It would be late and racy. The plugin host tells listeners at the moment the active set changes.

## Consequences

- There are four schedule-related fields instead of one; the REST API shows all of them.
- The scheduler engine is the only place the "paused" state exists. After a restart the state is recomputed from what is loaded.
- A plugin that is only absent for a moment causes a pause and a resume, visible in the log; stored data is untouched.
