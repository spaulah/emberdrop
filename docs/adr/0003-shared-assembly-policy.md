# ADR 0003: Shared assemblies come from the host; everything else is private

- Status: accepted
- Date: 2026-11-07

## Context

A plugin work unit must be usable as the host's `IWorkUnit`. That only works if the plugin and the host agree on what `IWorkUnit` is: two copies of the same assembly loaded into two contexts are two unrelated types and the cast fails. At the same time, plugins must be free to use their own versions of libraries, even versions that conflict with the host's or with each other.

## Decision

Assemblies are resolved in this order inside a plugin's load context:

1. **The .NET runtime's own assemblies** (anything that exists in the runtime directory) always come from the host.
2. **The shared list** always comes from the host's default context. By default it contains `Emberdrop.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Configuration.Abstractions` and `Microsoft.Extensions.Primitives`. Applications can add their own contract assemblies. A copy of a shared assembly inside a package is ignored.
3. **Everything else** is resolved from the plugin's `deps.json` (or from its directory when there is no `deps.json`) and loaded privately.

**The host version wins** for shared assemblies. Before loading anything, the host reads the assembly references of every managed assembly in the package from metadata (no code runs). If any of them references a shared assembly at a higher major or minor version than the host provides, the package is rejected with a message that names the assembly, the assembly that needs it, and both versions. Differences in build or revision are accepted. The manifest's `minAbstractionsVersion` is checked the same way.

## Alternatives rejected

**Let every plugin bring everything, including the contract.** Breaks type identity; the cast to `IWorkUnit` would fail.

**Share everything the host has.** Makes dependency conflicts impossible to solve and ties every plugin to the host's exact library versions.

**Let the plugin's version of a shared assembly win.** Would require the host to use a different version of its own libraries for that plugin, which is not possible for types the host already holds.

**Require exact version matches for shared assemblies.** Needlessly strict: a patch update of the host would reject every plugin.

**Discover the mismatch when the runtime fails to bind.** The failure would surface late, inside a work unit run, as a `FileLoadException` or `MissingMethodException` that names neither side's version.

## Consequences

- The shared list is the contract between hosts and plugins and should stay small and stable. Every entry is a compatibility promise.
- A plugin that is handed a type from a *non-shared* library by the host (for example a logger from a full logging implementation) will not see the same type. Only types from shared assemblies cross the boundary.
- Applications that want to give plugins extra capabilities add the assembly that defines them to `PluginHostOptions.SharedAssemblies`.
- ASP.NET Core framework assemblies are not on the default list. A plugin that depends on them privately gets its own copy; one that needs to exchange their types with the host must add them to the shared list.
