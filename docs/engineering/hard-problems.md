# Hard problems

Loading an assembly into a running .NET process takes one line. Replacing code that is running, without a restart and without losing work, is a different problem, I've been there so this is the reason why Emberdrop exists.

This page walks through the seven parts that were hard: what goes wrong, what Emberdrop does about it, how the tests prove it, and what is still not solved. The decision records in [docs/adr](../adr/) give the alternatives that were rejected; [known limits](known-limits.md) lists what the design cannot do.

## Type identity

**The problem.** In .NET, a type's identity includes the load context of its assembly. If the host and a plugin each load their own copy of `Emberdrop.Abstractions.dll`, there are two unrelated types called `IWorkUnit`, and casting the plugin's work unit to the host's interface fails. Every plugin package carries a copy of that assembly, because a normal build puts it in the output, so this is the default outcome and not a corner case.

**What Emberdrop does.** A short, explicit list of assemblies always resolves from the host: `Emberdrop.Abstractions`, a few `Microsoft.Extensions.*` abstractions, and the .NET runtime itself. The plugin's load context returns `null` for those names, which hands the request to the default context, and the copy inside the package is ignored. Everything else is private to the plugin. See [`PluginLoadContext`](../../src/Emberdrop.Hosting/PluginLoadContext.cs) and [ADR 0003](../adr/0003-shared-assembly-policy.md).

**What makes it hard.** The shared list is a compatibility promise. Each entry ties every plugin to the host's version of that assembly, so the list has to stay small. It also works in one direction only: an object whose type comes from a library that is not shared cannot cross the boundary, even when both sides have the same library at the same version.

**How it is proven.** `LoadAWorkUnitWhoseContractTypesAreTheHostsOwn` and `IgnoreTheCopyOfTheContractAssemblyThatEveryPackageCarries` in [PluginIsolationShould](../../tests/Emberdrop.Plugins.Tests/PluginIsolationShould.cs) run against real compiled plugins.

## Unload blockers

**The problem.** `AssemblyLoadContext.Unload()` does not unload anything. It asks for the context to be collected once nothing references it, and a single reference anywhere in the process keeps every assembly of the plugin in memory. The first implementation did not even get that far: it loaded plugins into the default context, where nothing is ever unloaded, and "unloading" only removed a dictionary entry ([ADR 0001](../adr/0001-collectible-load-context-per-plugin-version.md)).

The references that block an unload are rarely obvious. These are the ones this project ran into:

- **Exceptions.** An exception thrown by plugin code references the plugin's types through its stack trace. A scheduler that remembers its last error keeps the plugin loaded. A test found this one. The host now replaces a plugin's exception with a `PluginExecutionException` that carries the type, message and stack trace as text.
- **Generic services.** An `ILogger<MyWorkUnit>` resolved from the host's container is cached by the host, keyed by a plugin type. Each plugin version now has its own service provider, which is disposed with it.
- **Serializer caches.** `System.Text.Json` caches metadata for every type it reads or writes, in static state. Reading a plugin's typed input is enough to pin it. The host clears those caches during an unload through the handler the runtime itself uses for hot reload ([`SerializerCaches`](../../src/Emberdrop.Hosting/SerializerCaches.cs)). That handler is internal, so this is best effort.
- **Stack frames.** A local variable in a method that is still running counts as a reference, and the JIT may keep it alive longer than the source suggests. The host touches a loaded plugin only inside small methods marked `NoInlining`, so no reference survives in the caller's frame while the collection is checked.

**What Emberdrop does.** It verifies instead of assuming. After an unload the host forces a bounded number of collections (10 by default) and checks a `WeakReference` to the context. If the context is still alive, the API says so (`contextAlive`), and a diagnostic report lists the executions still running and anything the plugin declared. The host never retries forever and never forces anything.

**How it is proven.** `FreeThePluginsCodeWhenItIsUnloaded` checks the normal path. `DetectHostCodeThatKeepsAPluginTypeAlive` is a negative control: it caches a plugin type in a static dictionary and expects the check to fail, which shows the check can fail at all. `HandCallersTheFailureAsTextSoAKeptExceptionCannotPinThePlugin` holds a failure across an unload and still expects collection.

**What is not solved.** A plugin can pin itself, for example by subscribing to a static event or leaving a thread running, and the host cannot prevent that. The runtime offers no in-process way to list what roots a collectible context, so the report can only name what the plugin declared. The rest needs a heap dump; [known limits](known-limits.md#investigating-a-context-that-will-not-die) has the steps.

## Dependency conflicts

**The problem.** A plugin needs version 2 of a library and the host, or another plugin, has version 1. In a single load context only one can be loaded, so one side gets a version it was not built for and fails at some later moment with a `MissingMethodException`.

**What Emberdrop does.** Each plugin version resolves its private dependencies from its own `deps.json` into its own context, so any number of versions of a library can run side by side. The conflict that remains is on the shared list, where the host's version always wins. That one is checked before any code is loaded: the host reads the assembly references of every DLL in the package from metadata, and rejects the package if it needs a newer major or minor version of a shared assembly than the host has. The message names the assembly, the assembly that needs it, and both versions ([`SharedAssemblyPolicy`](../../src/Emberdrop.Hosting/SharedAssemblyPolicy.cs)).

**What makes it hard.** The failure the check prevents would otherwise appear late, inside a run, as a binding error that names neither version. Requiring an exact match would be simpler and would reject every plugin on each patch update of the host, so build and revision differences are accepted.

**How it is proven.** `RunAPluginWithItsOwnVersionOfALibraryNextToTheHostsDifferentVersion` loads a plugin whose library is at 2.0 while the host has 1.0. `RejectAPluginThatNeedsANewerSharedAssemblyThanTheHostHas` declares that same library shared and expects the rejection.

## File locks and partial installs

**The problem.** There are two, and both come from the file system.

On Windows, an assembly loaded from a path keeps its file open. The old version's files cannot be replaced or deleted until its context is collected, and collection can fail (see above). Every update becomes an ordering problem.

Packages arrive by being copied into a folder. A file event says that a file started to exist, not that its copy has finished. The first installer waited one second and then read the file, which is right for small files on a local disk and wrong otherwise. It also extracted straight into the final location and unloaded the old version before it knew the new one worked ([ADR 0007](../adr/0007-safe-install-and-last-known-good.md)).

**What Emberdrop does.**

- **Loads from memory.** Each assembly is read into a byte array and loaded from a stream, so no managed file is ever held open ([ADR 0002](../adr/0002-file-lock-free-plugin-loading.md)).
- **Waits for a complete file.** The size and modification time must hold still across consecutive checks, and the file must open exclusively, which fails while a writer still has it ([`StableFileDetector`](../../src/Emberdrop.Hosting/Installation/StableFileDetector.cs)).
- **Extracts into staging and promotes with one rename.** A crash leaves only staging debris, which is removed at startup.
- **Does not trust the archive.** Entry names are checked before any byte is written, and the limits are enforced on the bytes actually written, because archive headers can lie.
- **Proves before it replaces.** A replacement for an installed version is first loaded into a throwaway context: its assemblies load, its module initializers run, its work units resolve and its services build. Only then is the installed copy retired. A package that fails is quarantined with every reason listed, and whatever was running keeps running.

**How it is proven.** [PluginInstallerShould](../../tests/Emberdrop.Plugins.Tests/PluginInstallerShould.cs) starts an install while only part of the file exists, holds a complete file open for writing, feeds the extractor zip-slip names and a zip bomb, and installs versions whose entry assembly throws on load, whose services cannot be built, and whose manifest names a work unit that does not exist. In each case the previous version stays in service.

**What is not solved.** Loading from memory means `Assembly.Location` is empty for plugin code. Native libraries can only be loaded from a path, so they still lock their file on Windows. The no-lock tests run on macOS and Linux, where they prove the mechanism (nothing is loaded from a path) and not the Windows symptom; a Windows CI run is still missing.

## Version binding

**The problem.** A stored work unit has to keep working when its plugin is updated, renamed inside, or absent for a while. The first design stored the class name and looked for it in whatever was loaded. Two plugins can have a class with the same name, renaming the class breaks every stored work unit, there is no way to stay on a version, and a plugin that is briefly missing becomes a permanent failure ([ADR 0005](../adr/0005-version-binding-for-persisted-work-units.md)).

**What Emberdrop does.** A stored work unit is bound to three things: a plugin id, a logical name from the manifest, and a version constraint (`*`, an exact version, or `^1`). No CLR type name is stored for a plugin work unit. The binding is resolved each time the work unit fires, against the versions that are active at that moment. A work unit created from a manifest follows its plugin within the major version it started on; moving to the next major version is the operator's decision.

**What makes it hard.** Three cases needed a deliberate answer.

- **Nothing matches.** The work unit waits. Its status says why in words, its schedule is paused, and the missed run repeats when a matching version loads. The status is computed from what is loaded and never stored, so it cannot go stale.
- **The stored input was written for another schema.** The run is refused before it starts, and the error names both schema versions. Input that is wrongly parsed looks like success, which is worse than a failure.
- **Work units stored before bindings existed.** They are still found by name and are adopted into the new model the first time a matching plugin loads.

**How it is proven.** [WorkUnitBindingShould](../../tests/Emberdrop.Plugins.Tests/WorkUnitBindingShould.cs) resolves each constraint form against several loaded versions and checks the exact wording of the reason when nothing matches.

**What is not solved.** A missed run that is waiting to repeat is held in memory. Recurring schedules resume after a restart, but a missed one-off run does not.

## Draining

**The problem.** When version 2 arrives, version 1 may be in the middle of something. Its code cannot be freed while it runs, new work must go to version 2 at once, and .NET has no safe way to stop a thread. There is also a race: an execution can be starting on version 1 at the moment version 1 is being retired.

**What Emberdrop does.** Every execution takes a lease on its version, under the same lock that decides whether the version still accepts work. So an execution either got its lease before draining began, and the version waits for it, or it is routed to another version. No execution starts on a version that is going away.

The new version is active before the old one starts draining, so there is always a version to take work. The old one gets a grace period (30 seconds by default) in which running work is left alone, then a cancellation request and a second period. It unloads when its last lease is released ([ADR 0004](../adr/0004-draining-and-no-forced-abort.md)).

**What makes it hard.** The case where the work does not stop. Emberdrop aborts nothing, because a forced stop turns a slow work unit into a corrupted one. The default is to leave the version in `Draining`, flag it as stuck, and let it unload by itself when the work ends. The alternative policy reports the remaining executions to their callers as failed. That policy admits what it cannot do: the code is still running, so the context cannot be freed, and the report names those executions.

**How it is proven.** `RunAThousandWorkUnitsWhileVersionsAreSwappedWithoutLosingOrRepeatingAny` in [PluginDrainingShould](../../tests/Emberdrop.Plugins.Tests/PluginDrainingShould.cs) starts 1,000 executions while six versions are swapped underneath them. It checks that each ran exactly once, that the work spread across several versions, and that all five retired versions were collected. Other tests use work units that block, that ignore cancellation, and that leave a thread behind.

**What is not solved.** A work unit that ignores its cancellation token keeps its version loaded for as long as it runs. Emberdrop reports this and does not fix it.

## Schedule reconciliation

**The problem.** A plugin's manifest suggests a schedule. Operators change schedules, switch work units off and pin versions. Plugins update and disappear at any time. With a single schedule column there are two outcomes, and both are wrong: either a plugin update overwrites the operator's choice, or the operator's first edit freezes the schedule and the plugin can never improve its default again.

**What Emberdrop does.** It records who owns each field ([ADR 0006](../adr/0006-schedule-reconciliation.md)). The plugin owns the default schedule. The operator owns the override, the enabled flag, the version constraint and the input, and a plugin update never changes those. The effective schedule is the override if there is one and the default otherwise.

When the last active version of a plugin goes away, its work units are paused: their schedules are removed from the scheduler and kept in the store, and they are put back when a matching version returns. Nothing is deleted. A work unit that a newer version no longer declares stays in the store with a status that says why it cannot run.

**What makes it hard.** Mostly the details of what counts as an operator's decision. The REST update sends the whole work unit back, so an unchanged schedule must not become an override, or editing a description would freeze the schedule. Setting the schedule back to the default clears the override. Pausing cannot use the enabled flag, because then "the operator switched this off" and "the plugin is away" would look the same. Reconciliation is triggered by the plugin host at the moment the active set changes, and changes are processed one at a time, so two plugins arriving together cannot register the same work unit twice.

**How it is proven.** [PluginReconciliationShould](../../tests/Emberdrop.Plugins.Tests/PluginReconciliationShould.cs) covers the override surviving an update, a new default flowing through when there is no override, and schedules pausing and resuming as a plugin leaves and returns.

**What is not solved.** The paused state exists only in the scheduler and is recomputed from what is loaded after a restart. A manifest's default schedule is checked only for its shape, so a schedule the engine rejects is found when the work unit is scheduled and not when the package is validated.
