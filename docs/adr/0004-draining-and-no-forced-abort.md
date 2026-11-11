# ADR 0004: Retire plugin versions by draining, and never abort running code

- Status: accepted
- Date: 2026-11-07

## Context

When a plugin version is replaced or removed, some of its work units may be running. Its code cannot be freed until they are done, and the new version must start taking work immediately. The choices are what to do with the running work units, how long to wait, and what to do when they will not end.

## Decision

**Executions hold leases.** Every execution of a plugin work unit takes a lease on the version it runs on, under the same lock that decides whether the version is still accepting work. A version in the `Draining` state accepts no new leases. A version can only reach `Unloading` when its lease count is zero. This closes the race between "start a work unit on version 1" and "unload version 1": a work unit either got a lease before draining began (and the version waits for it) or was routed to another version.

**Several versions can be active at once.** A new execution goes to the newest active version that provides the work unit unless it is pinned to one. During an update the new version is active before the old one starts draining, so there is never a moment with no active version.

**Draining has two timed phases.**

1. *Grace period* (default 30 s): the running executions are left alone and may finish normally. This is what makes a hot update safe for work that is half done.
2. *Cancellation period* (default 30 s): the executions' `CancellationToken` is signalled. Well-behaved work units stop and the version unloads.

The timeouts apply to waiting for executions. Once none are left, unloading and its bounded verification are always completed, so a version with nothing running is never reported as stuck.

**When executions outlast both periods, the policy decides**, and the default forces nothing:

- `LeaveDraining` (default): stop waiting, leave the version in `Draining`, set a visible *stuck* flag with a diagnostic report. The version unloads on its own the moment its last execution ends.
- `KeepWaiting`: do not declare it stuck; the call returns only when the executions end.
- `FailStuckExecutions`: report the remaining executions to their callers as failed (`PluginExecutionAbandonedException`) and stop counting them, so the version can proceed to unload. This is explicit and visible, and it is honest about the limit: the code is not stopped. If it is still running, the load context cannot be freed and the report names those executions.

**Nothing is ever aborted.** There is no `Thread.Abort`, no thread interruption and no process-level kill.

**A plugin's exception never leaves the plugin boundary as an object.** The host replaces it with a `PluginExecutionException` that carries the type, message and stack trace as text, and a cancelled run with a fresh `OperationCanceledException`.

## Alternatives rejected

**Abort or interrupt running work units after a timeout.** .NET has no safe way to do this. `Thread.Abort` does not exist in modern .NET; interrupting a thread only works while it blocks and can leave locks and invariants half-done. A forced stop would turn a slow work unit into a corrupted one.

**Unload immediately and let running work units fail.** Simple, and correct for the *plugin removed on purpose* case, but wrong for the common one: deploying a new version must not destroy work that is mid-way. (An operator who wants that can set the grace period to zero.)

**Cancel as soon as draining starts.** Defeats the point of a hot update for work units that would have finished in seconds. Cancellation is the second phase, not the first.

**Wait forever with no signal.** A single misbehaving work unit would hold a version, and everything it pins, for the life of the process, with nothing to tell the operator.

**Requeue the interrupted executions on the new version.** Only an engine that can safely re-run a work unit can offer this, and no engine in this project declares such a capability. A policy nothing can honor would be a promise the system cannot keep, so it is not offered. If an engine gains a "requeue" capability, it can be added as a fourth stuck policy without changing the rest.

**Let the caller see the plugin's own exception.** Convenient for diagnostics, but an exception thrown by plugin code references the plugin's types through its stack trace. Any scheduler that remembered its last error would keep the plugin's load context alive, and this is not hypothetical: a test found it. Text carries what an operator needs without the reference.

## Consequences

- A hot update is safe for in-flight work, bounded in time, and observable: the state, lease count and stuck flag are in the API.
- A work unit that ignores cancellation keeps its version loaded as long as it runs. That is the plugin author's bug to fix, and the product reports it instead of hiding or "fixing" it.
- Stack traces of plugin failures reach the scheduler as text, not as a chain of exception objects; code that inspects inner exceptions will find none.
- `FailStuckExecutions` can leave a load context alive (a leak) by design; it trades memory for availability and says so.
