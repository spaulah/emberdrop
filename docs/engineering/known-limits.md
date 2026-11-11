# Known limits

What Emberdrop's plugin hosting does **not** do, stated plainly. Behavior that arrives in later steps of the plugin-hosting work is marked *(planned)*.

## It is not a security sandbox

Plugins are arbitrary .NET code running inside the host process with the host's permissions. A plugin can read any file the host can read, open network connections, read the host's configuration and secrets, start processes, and call into any host assembly it can reach. Loading a plugin into its own `AssemblyLoadContext` separates *dependencies* and makes *unloading* possible; it does not restrict *capabilities*. .NET has no in-process sandbox.

Consequences:

- Only load packages you trust. Restrict who can write to watched folders.
- The optional package hash allowlist limits **which** packages load, not what they do once loaded. It is not code signing: it proves a file matches a hash you wrote down, not who produced it.
- If you need to run code you do not trust, run it in a separate process or container. That is a different architecture; see [ADR 0001](../adr/0001-collectible-load-context-per-plugin-version.md).

## Unloading is cooperative and can fail

A load context can only be freed when nothing references code or types from it. The host never holds such references (and tests check that), but a plugin can, and the host cannot stop it. Common causes:

- **Static events or static fields** that the host or the runtime reaches: a plugin subscribes to a host event, `AppDomain.CurrentDomain.UnhandledException`, `AssemblyLoadContext.Default.Resolving`, a `Console` or `PosixSignal` handler, and never unsubscribes.
- **Threads, timers and thread-pool work** started by the plugin that are still running. A running thread's stack references plugin code.
- **Caches in shared libraries** that key by `Type`: serializers (`System.Text.Json` metadata, Newtonsoft contract resolvers; for the host's own use of `System.Text.Json` on a plugin's input and output types, schema generation included, the host clears the runtime's serializer caches while it unloads a version, through the internal handler the runtime uses for hot reload; if a future runtime removes that handler the host cannot do it and an unload will report the plugin as not freed. Your plugin's *own* use of a serializer is yours to clean up), `Activator`/DI caches, logging category caches, `ArrayPool` or `ConditionalWeakTable` entries holding plugin objects.
- **Thread-locals and `AsyncLocal` values** on threads that outlive the plugin.
- **Finalizable objects** whose finalizer has not run yet.
- **Native libraries.** See below.

When the context does not die, Emberdrop reports it (log and result) after a bounded number of forced collections; it does not retry forever and does not force anything. The memory stays allocated until the process restarts.

## Code that does not stop cannot be stopped

Emberdrop never aborts a thread. .NET has no safe equivalent of `Thread.Abort`, and the design deliberately does not try to build one ([ADR 0004](../adr/0004-draining-and-no-forced-abort.md)). A work unit that ignores its `CancellationToken`, blocks forever, or spins keeps running, and keeps its version loaded.

What the host does about it: the version leaves service (no new work), the work unit gets a grace period, then a cancellation request, then the version is flagged **stuck** and stays in the `Draining` state. It unloads by itself when the work unit finally ends. With the `FailStuckExecutions` policy the work unit is reported to its caller as failed and no longer counted, so the version can proceed to unload, but its code is still running, so the runtime cannot free the load context until it ends. Plugin authors are responsible for honoring cancellation. A plugin that starts its own threads is responsible for stopping them.

## Exceptions from plugin work units arrive as text

The host never lets an exception object created by plugin code reach the scheduler: it replaces it with a `PluginExecutionException` that has the original type, message and stack trace as text (and `OperationCanceledException` for a cancelled run). A work unit's exception references the plugin's types through its stack trace, so any holder of it would keep the plugin's load context alive. The cost: inner exceptions are not available to code that handles work unit failures; use `PluginExecutionException.Details`.

## The install pipeline

- **The extraction limits are heuristics, not guarantees.** Entry count, total size and expansion ratio (defaults 5,000 entries, 500 MB, 100x) stop the ordinary zip-bomb and zip-slip shapes and are enforced on the bytes actually written, but they do not make an archive safe to *run*. They protect the extraction step only.
- **Extraction does not scan the content.** A package can still contain malware; a hash allowlist, access control on the watched folders and trusting the author are what defend against that.
- **A same-version replacement is loaded twice.** To keep the installed copy in service if the replacement is bad, the replacement is first loaded into a throwaway context to prove it activates, then loaded for real. The plugin's module initializer and startup code therefore run twice for such a package, once in a context that is discarded. Different-version installs are loaded once.
- **Restoring after a failed replacement is best effort.** If the trial passes but the real activation then fails (a race, a full disk), the previous copy is moved back from the archive and loaded again; if that fails too it stays in the archive and the log says so.
- **Quarantine is never pruned.** Rejected packages and their reason files stay until someone deletes them. The archive has a retention limit; quarantine does not.
- **File events on network shares are unreliable.** The watcher uses the operating system's change notifications; on some network file systems they are missed. Stable-file detection protects against reading half-written files, not against events that never arrive. A restart picks up whatever is in the folder.
- **A file that never settles is deferred, not rejected.** If a file keeps changing or stays locked past `StableFileTimeout` (60 s by default), the installer leaves it where it is and logs a warning; the next event for it tries again.

## Native libraries

Native libraries are loaded from a file path, never from memory:

- On Windows, the file stays locked while the library is loaded, which defeats the "delete the old version's directory" guarantee for plugins that ship native code.
- .NET offers no reliable way to unload a native library that managed code in a collectible context loaded, so such a plugin's native code may stay mapped after the context is collected.
- Native libraries are resolved from the plugin's `deps.json` (`runtimes/<rid>/native`). The host does not validate them.

## `Assembly.Location` is empty

Plugin assemblies are loaded from memory (see [ADR 0002](../adr/0002-file-lock-free-plugin-loading.md)). A plugin that finds its own folder through `Assembly.Location` gets an empty string and must receive paths another way.

## What the tests can and cannot prove

- The collectibility tests prove that the host's own code paths (load, create a work unit, run it, unload) leave nothing behind, using a `WeakReference` and a bounded garbage-collection loop. A negative-control test shows that the check *can* fail.
- "No file locks" can be proven on Windows only by running the tests on Windows. On macOS and Linux the tests prove the mechanism (nothing is loaded from a path, `Assembly.Location` is empty, the directory can be deleted while loaded) rather than the Windows symptom. A Windows CI work unit is the right addition.
- Collection depends on the garbage collector. The tests do not assert *when* it happens, only that it happens within a bounded number of forced collections.

## Shared assemblies

- Only assemblies on the shared list cross the host/plugin boundary with identical types. A host that hands a plugin an object whose type comes from a non-shared library will see a failed cast.
- ASP.NET Core framework assemblies are not shared by default.
- The host version always wins. A plugin built against a newer major or minor version of a shared assembly is rejected, not adapted.

## Plugin services

- Every plugin version has its own service provider, so `ILogger<MyWorkUnit>` and other generics over a plugin's types are owned by the plugin and freed with it. (They were a known pin in an earlier step, when work units were created with the host's container.)
- **Only singletons can be offered from the host.** `PluginHostOptions.HostServices` resolves the service from the host's root provider once, when the plugin's provider is built, and hands the plugin that instance. A host service with a scoped or transient lifetime is not available to plugins. Offered services must also come from a shared assembly, or the plugin cannot name the type.
- **Configuration is a snapshot.** A plugin sees the values its declared keys had when its provider was built. A host setting that changes later is not visible to a running plugin version until it is replaced.
- **Plugin-registered hosted services are not started.** `IHostedService` registrations in a plugin's container are ignored. Long-lived background work belongs in a work unit, or must be started and stopped by the plugin itself and declared through `IPluginActivity` so the diagnostic report can name it.
- The plugin's `startup` code runs inside the host process with the host's permissions like all other plugin code (see above).

## Stored work units that wait

- A run that fires while its plugin is not available is recorded by the scheduler engine as completed, because it did nothing and did not fail; the work unit's `status` and the log say what happened. The repeat of that run is held **in memory**: after a restart the plugins load first and recurring schedules continue, but a missed one-off run is not remembered.
- A work unit's status is computed from what is loaded now. It says nothing about the past.
- Prerelease plugin versions are ordered by Semantic Versioning precedence and can be selected by `*`; use a pin or `^` constraint to avoid that.
- Manifest default schedules are checked only for shape (5 or 6 fields). A schedule the scheduler engine rejects is rejected when the work unit is scheduled, not when the plugin is validated.

## What the diagnostic report can and cannot tell you

`GET /api/plugins/{id}/{version}/report` (and the log line written when a context will not die) contains:

- the lifecycle state, the stuck flag, and whether the runtime still holds the load context;
- the executions still running on that version, including executions the host stopped waiting for and whose code is still running;
- anything the plugin **declared** through `context.Activity.Track(...)`: threads, timers, subscriptions;
- the usual causes of a pinned context, and how to take a heap dump.

It **cannot** tell you which static event, thread or cache is holding the context if the plugin did not declare it. The .NET runtime offers no in-process way to list the roots of a collectible context, and the host does not guess. A "context still alive" with an empty execution and activity list means: something the host cannot see holds it. The tests include both cases: a thread the plugin declared is named in the report, and a static-event subscription that nothing declares is detected (the context stays alive) but only described by its common causes.

### Investigating a context that will not die

1. Confirm it with `GET /api/plugins`: the version is `Unloaded` and `contextAlive` is `true`.
2. Install the tool and take a heap dump of the running host:

   ```bash
   dotnet tool install -g dotnet-gcdump
   dotnet-gcdump ps                                  # find the host's process id
   dotnet-gcdump collect -p <pid> -o plugin.gcdump
   ```

3. Open `plugin.gcdump` in Visual Studio (or PerfView). Search for types from the plugin's assemblies (for example the work unit class) and for the `AssemblyLoadContext` named `plugin:<id>@<version>`.
4. Follow *paths to root* (in Visual Studio: select the instance, then the retention graph). The first object on that path that is **not** from the plugin is the holder: a static field, an event on a host or framework object, a timer, a thread, a cache.
5. Fix it in the plugin (unsubscribe, stop the thread, dispose the timer) or, if the holder is host code, in the host. Then repeat steps 1–2 to confirm the context is gone.

A dump shows what is held at that moment. A context can also be held only briefly by a pending finalizer or timer callback; the host retries with forced collections before it gives up, so a persistent "alive" is a real holder.
