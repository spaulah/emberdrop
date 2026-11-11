# Vocabulary

Emberdrop is a set of building blocks for hosting versioned, hot-loadable code, not a job runner. Running work on a schedule is one use of those blocks. These are the words the code, the API and the documentation use. [ADR 0008](../adr/0008-work-units-not-jobs.md) explains why.

| Term | Meaning |
| --- | --- |
| **Work unit** | A unit of executable code provided by a plugin. Identified by the plugin id plus a logical work unit name (never a class name). Has a manifest entry, optional input and output types, and declared triggers and capabilities. In code it is an `IWorkUnit`. |
| **Plugin package** | The versioned `.emberdrop` archive that contains one or more work units and their private dependencies. |
| **Manifest** | The declarative description of a package and its work units (`emberdrop.json`; [JSON Schema](../manifest.schema.json)). |
| **Trigger** | What causes an execution. v0.1 supports exactly three: **Manual** (on demand), **Delayed / one-off** (once, at a time) and **Recurring** (cron). Triggers are polymorphic (`Trigger` and its subtypes) so others can be added later; see [Triggers](triggers.md). |
| **Execution** | One run of a work unit. Has an id, the plugin version it is bound to, an input, a state, a result and timing. (Earlier versions called this a "job run".) |
| **Host** | The process that loads packages, binds versions and runs executions. |
| **Provider** | A pluggable scheduler or store implementation (`IWorkScheduler`, `IWorkStore`). |

## What each term is called in code

| Concept | Type or name |
| --- | --- |
| The contract a plugin implements | `IWorkUnit`; most plugins derive from `WorkUnit<TInput, TOutput>` and use `NoInput` / `NoOutput` when there is none |
| What a work unit receives | `WorkContext` |
| What a work unit returns | `WorkResult` (succeeded, failed with an error code and message, or succeeded with an output) |
| A stored work unit (its binding, schedule, input) | `WorkUnitDefinition`, in the `WorkUnitDefinition` table |
| What caused an execution | `Trigger`: `ManualTrigger`, `OneOffTrigger`, `RecurringTrigger` |
| An execution's id, state and summary | `ExecutionId`, `ExecutionState`, `ExecutionInfo` |
| What a scheduler is asked to run | `WorkInvocation` |
| A scheduler provider / a store provider | `IWorkScheduler` / `IWorkStore` |
| The service that coordinates discovery, registration and scheduling | `WorkUnitManager` |
| The REST resource | `/api/work-units` |

## What a work unit receives

`WorkContext` has exactly these members:

| Member | Meaning |
| --- | --- |
| `ExecutionId` | The id of this execution. |
| `WorkUnitName`, `PluginId`, `PluginVersion` | Which work unit this is and which plugin version it is bound to. The plugin members are `null` for a work unit built into the host. |
| `Trigger` | What caused the execution. |
| `Logger` | An `ILogger` for the execution. |
| `Services` | A service provider scoped to the execution. For a plugin work unit it is the plugin's own container. |
| `Configuration` | The values of the configuration keys the manifest declares for the work unit. Other keys are not visible. |
| `Input` | The input as JSON, or `null`. `WorkUnit<TInput, TOutput>` reads it for you. |
| `Activity` | Optional hook to tell the host about long-lived things the work unit starts (threads, timers), so they can be named if the plugin cannot be unloaded. |

## Where the word "job" is still used

Only where it is somebody else's word or ordinary English: inside the Hangfire adapter (Hangfire calls its units of work jobs, and `Emberdrop.Hangfire` uses Hangfire's API), in historical notes such as [the original design document](../design/provider-agnostic.md), and in plain prose. It is not used as a type, namespace, table, endpoint or UI label.
