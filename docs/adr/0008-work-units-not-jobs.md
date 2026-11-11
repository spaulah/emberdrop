# ADR 0008: Work units, not jobs

- Status: accepted
- Date: 2026-11-08

## Context

Emberdrop started as a way to load Hangfire jobs from packages at runtime, and everything was named after that: `IEmberdropJob`, `JobManager`, `IJobScheduler`, a `SystemJob` table, `/api/jobs`. The project has since become something wider: a set of building blocks for hosting versioned, hot-loadable code (loading into isolated contexts, binding stored work to a version, draining and unloading). A scheduler is one consumer of those blocks. A plugin can reasonably provide something a scheduler never starts, such as a function called on demand.

"Job" carries a meaning that no longer fits. It implies background and scheduled work, it is the name Hangfire gives to its own units (so every sentence about the Hangfire adapter was ambiguous), and it would make every later kind of executable thing look like a special case of a job.

## Decision

**The core abstraction is the work unit**: a versioned, hot-loadable, self-describing piece of executable code that a host can discover, bind to a version, trigger, drain and unload. A job that runs on a schedule is one work unit with one kind of trigger.

**What a job used to be is split into separate ideas.** The code is the *work unit*. What starts it is a *trigger* (manual, one-off, recurring in v0.1; polymorphic so more can be added). One run is an *execution* (previously "job run"). The full list is in [the vocabulary](../concepts/vocabulary.md).

**`IWorkUnit.ExecuteAsync` returns a `WorkResult`** (succeeded, failed with a code and message, or succeeded with an output) and receives a `WorkContext`. Typed input and output are optional and provided by the `WorkUnit<TInput, TOutput>` base class, so the interface stays small and nothing crosses a plugin's load context except types from `Emberdrop.Abstractions` and the BCL. The output travels as JSON text for the same reason the input does.

**The old names are removed, not aliased.** The project has no release and no users, so there are no deprecated aliases and no compatibility shims. The initial database migrations were rewritten with the new table and column names instead of adding rename migrations, so the migration history starts clean. Earlier ADRs were updated to the new words so that they read consistently with the code; their decisions are unchanged.

**"Job" survives in three places only**: inside the Hangfire adapter, where it is Hangfire's own word; in historical notes (the original design document); and in plain English. It is not used as a type, namespace, table, endpoint or UI label.

## Consequences

- Hosts and tools can talk about "a work unit with a Manual trigger" or "a work unit with no triggers" without stretching the word job.
- The vocabulary reads the same in the manifest, the API, the database and the UI.
- Anyone who knew the old names must learn the new ones. This is the cheapest moment to do it: before the first release.
- The Hangfire adapter now has two words that mean different things (a Hangfire *job* is how Hangfire stores one *execution*). That is accurate, and it stays inside the adapter.

## Alternatives considered

**Keep "job" and document that it means more than scheduled work.** Cheapest, but the word keeps pulling readers back to the narrow meaning, and the Hangfire ambiguity stays.

**"Task".** Collides with `System.Threading.Tasks.Task`, which appears in every signature of the interface.

**"Function" or "action".** Implies a call with a return value, or a UI event. A work unit has an identity, a version, a manifest and a lifecycle, which neither word suggests.

**Rename but keep the old names as obsolete aliases.** There is nobody to be compatible with.
