# ADR 0010: The hosting core knows nothing about work units

- Status: accepted
- Date: 2026-11-09

## Context

Loading versioned code into isolated, collectible contexts, handing out leases, draining, unloading and quarantining are useful on their own. They were written inside `Emberdrop.Core` next to the things that make a plugin export a *work unit*: binding stored work units to versions, schedules, triggers, typed input and output, the scheduler and the store. The two were tangled in a few places (`PluginHost` knew `IWorkUnit` and `WorkContext`, the manifest knew triggers and capabilities, the service provider factory knew which configuration keys a work unit requires), so the hosting could not be reused without everything else.

## Decision

**A new project, `Emberdrop.Hosting`, contains everything about packages and runtime isolation and nothing else.** It depends on `Emberdrop.Abstractions` only (for `PluginVersion`, the version constraint and the activity hook), the runtime, and the Microsoft.Extensions abstractions. It sees *exports*: a name, a kind string and a type in a load context. Its manifest is package metadata plus an opaque list of exports.

**Work-unit meaning is plugged in through two seams, both owned by the code that wants the meaning.**

- `ManifestFormat` names the manifest property that lists exports and lets the host check the properties of an export that the core does not understand.
- `IExportHandler` is called once per export while a package loads. It can refuse the package and can keep plain data about the export (`ExportDescriptor.Metadata`); it also names the configuration keys an export may see.

**Running is `ExecuteExportAsync(choose, run, preflight)`.** The host passes in the policy (`choose` picks the export of an active version; it runs under the same lock as taking the lease, so a version cannot be retired between choosing and leasing) and the code to run against a fresh instance and a service scope of the plugin's own provider. Version binding therefore stays in `Emberdrop.Core`, exactly as before, and the lease, drain, abandon and exception-as-text behaviour stay in the hosting core, exactly as before.

**`Emberdrop.Core` is the work-unit layer on top.** It supplies the work-unit manifest format and export handler, version binding, the reconciler, the runner, the manager and the services that talk to schedulers and stores. Types that only exist for work units moved with it (`WorkUnitBinding`, `PluginWorkUnitDescriptor`, the schema-mismatch and missing-configuration exceptions, the missing-plugin policy), and their options moved to `WorkUnitOptions`.

**The rule is enforced by a test.** `tests/Emberdrop.Architecture.Tests` reads the metadata of `Emberdrop.Hosting` and fails if it references `Emberdrop.Core`, a scheduler, a store, Hangfire, or any work unit, trigger, scheduling or storage type, and it proves the check works by running it on `Emberdrop.Core`.

## Consequences

- Behaviour did not change: the existing plugin tests pass unmodified apart from where they build options or name types that moved. New tests show the hosting core running with no work-unit handler.
- `PluginWorkUnitException` is now `PluginExecutionException` (it is the hosting core's), and `RunningExecutionInfo.WorkUnitName` is `ExportName`. The REST DTOs keep `workUnitName`.
- Code that configures hosting from the container now has two options objects: `PluginHostOptions` (shared assemblies, storage, drain) and `WorkUnitOptions` (work unit kinds, trigger kinds, missing-plugin policy). `ConfigurePlugins` and `ConfigureWorkUnits` configure them; `PluginHostOptions.UseWorkUnits` wires the work-unit format and handler for hosts that build the options by hand.
- The hosting core can be packaged and used without Emberdrop's scheduling and storage. It is not yet published as a separate package.

## Alternatives considered

**Keep one project and rely on namespaces.** Cheap, but nothing stops the dependency creeping back; an assembly boundary plus a metadata test does.

**Make the hosting core generic over an export type (`PluginHost<TExport>`).** Pushes generics through every signature, and the interesting part, the type, only exists inside the plugin's load context.

**Let the hosting core resolve version constraints.** Constraints and the "newest active version" policy are how stored work units follow their plugin, which is work-unit semantics; the core offers the choice as a callback instead.
