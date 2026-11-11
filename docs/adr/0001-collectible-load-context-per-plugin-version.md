# ADR 0001: One collectible load context per plugin version

- Status: accepted
- Date: 2026-11-07

## Context

Emberdrop's purpose is to load, replace and remove work unit code in a running process. The first implementation loaded every plugin assembly with `Assembly.LoadFrom` into the default `AssemblyLoadContext`. That cannot work as advertised:

- Assemblies in the default context are never unloaded. "Unloading" only forgot a dictionary entry, so old code stayed in memory forever.
- A second assembly with the same name cannot be loaded into the same context, so a new version of a plugin either returned the old code (same version) or failed (different version).
- All plugins and the host share one namespace of assemblies, so two plugins that need different versions of a library cannot both run.

## Decision

Each (plugin id, version) gets its own **collectible** `AssemblyLoadContext`. It resolves the plugin's private dependencies from the plugin's own `deps.json` using `AssemblyDependencyResolver`. A small, explicit set of assemblies (the contract) always resolves from the host's default context, so types that cross the boundary are identical on both sides. See [ADR 0003](0003-shared-assembly-policy.md).

Unloading is verified, not assumed: after `Unload()` the host forces a bounded number of garbage collections and checks, through a `WeakReference`, that the context really died. If it did not, the host reports it. It never forces anything.

## Alternatives rejected

**A process per plugin.** The strongest isolation: a crashing or runaway plugin cannot take the host down, and memory is returned to the OS when the process exits. Rejected for now because every work unit invocation would need a serialization boundary and a process lifecycle manager, which is a different and much larger system, and it would make the in-memory and test paths heavier. It remains the right answer for *untrusted* plugins; this decision does not rule it out later. Isolation by load context is about dependencies and unloadability, **not** security (see [known limits](../engineering/known-limits.md)).

**`AppDomain`s.** Not available in modern .NET.

**Keeping the default context (the previous design).** Cannot unload and cannot hold two versions.

**One shared collectible context for all plugins.** Unloading one plugin would unload all of them, and dependency conflicts between plugins would remain.

**A new non-collectible context per version.** Solves dependency conflicts and side-by-side versions but leaks every old version for the life of the process.

## Consequences

- Several versions of one plugin can be loaded at the same time, each with its own copies of its libraries.
- Unloading depends on the host and the plugin holding no references into the context. Anything that does (static events, running threads, timers, caches keyed by plugin types) keeps it alive. The host therefore never stores a plugin `Type`, `Assembly` or instance outside the object that owns the context, and has a test that fails if it does.
- A context that cannot be collected leaks memory until the process restarts. This is detected and reported, but cannot always be fixed from the outside.
