# Architecture

Emberdrop is built in layers. Each layer uses the ones below it and nothing above.

```
┌───────────────────────────────────────────────────────────────────┐
│  REFERENCE HOST                               Emberdrop.Server    │
│  REST API (/api/work-units, /api/plugins)                         │
├───────────────────────────────────────────────────────────────────┤
│  PROVIDERS                                                        │
│  Schedulers: InMemory, Hangfire                                   │
│  Stores: SQLite, SQL Server, InMemory                             │
├───────────────────────────────────────────────────────────────────┤
│  WORK-UNIT SEMANTICS                                Emberdrop.Core│
│  discovery, registration, scheduling, version binding,            │
│  reconciliation, triggers, typed input and output                 │
├───────────────────────────────────────────────────────────────────┤
│  HOSTING CORE                                   Emberdrop.Hosting │
│  packages, load contexts, lifecycle, drain and leases,            │
│  quarantine, diagnostics (no work units, no triggers)             │
├───────────────────────────────────────────────────────────────────┤
│  ABSTRACTIONS                               Emberdrop.Abstractions│
│  IWorkUnit, WorkContext, Trigger, IWorkScheduler, IWorkStore      │
└───────────────────────────────────────────────────────────────────┘
```

| Layer | Project | What it is for | May depend on |
| --- | --- | --- | --- |
| Reference host | `Emberdrop.Server` | An ASP.NET host with a REST API that wires the layers together. One way to run Emberdrop, not the only one. | everything |
| Providers | `Emberdrop.InMemory`, `Emberdrop.Hangfire`, `Emberdrop.Storage.Relational`, `.Sqlite`, `.SqlServer` | Pluggable schedulers (`IWorkScheduler`) and stores (`IWorkStore`). | Abstractions, and Core for the invoker contract a scheduler calls |
| Work-unit semantics | `Emberdrop.Core` | Interprets a package's exports of kind `work-unit` as work units: discovers and registers them, binds stored work units to plugin versions, reconciles schedules, creates executions, talks to the scheduler and the store. | Hosting, Abstractions |
| Hosting core | `Emberdrop.Hosting` | Everything about packages and runtime isolation: staging, validation, manifest reading, collectible load contexts, lifecycle states, drain and lease tracking, quarantine, diagnostics. | Abstractions |
| Abstractions | `Emberdrop.Abstractions` | What plugin authors and provider authors compile against. | nothing |

## The hosting core on its own

`Emberdrop.Hosting` does not know what a work unit or a trigger is. It sees **exports**: a name, a kind (a string) and a type in a load context. What an export is *for* is decided by the host that uses it, through two small hooks:

- **`ManifestFormat`** says which manifest property lists the exports and how to check the properties of an export the core does not know about.
- **`IExportHandler`** is called once per export while a package loads. It can refuse the package and can keep plain data about the export (`ExportDescriptor.Metadata`). It also says which host configuration keys an export may see.

To run code, the host calls `PluginHost.ExecuteExportAsync`, giving it a function that **chooses** which export of which active version to use (it runs under the core's lock together with the lease being taken, so the chosen version cannot be retired in between) and a function that **runs** against a fresh instance and a service scope of the plugin's own provider. The version stays loaded until the run ends; draining waits for it.

`Emberdrop.Core` supplies the work-unit versions of both hooks (`PluginWorkUnitManifest.Format`, `WorkUnitExportHandler`) and the choosing and running (`PluginHostWorkUnitExtensions.ExecuteWorkUnitAsync`).

## How the rule is enforced

`tests/Emberdrop.Architecture.Tests` reads the metadata of `Emberdrop.Hosting` and fails if it:

- references any assembly other than `Emberdrop.Abstractions`, the runtime and the Microsoft.Extensions abstractions (so no `Emberdrop.Core`, scheduler, store, Hangfire or database library), or
- refers to a work unit, trigger, scheduler or store type of the Abstractions, anywhere (signatures, method bodies), or names one in its public surface.

It also checks that providers do not reference the reference host or the hosting core, and that the check itself finds what it should when pointed at `Emberdrop.Core`. `HostingCoreAloneShould` in the plugin tests runs the hosting core with no work unit handler at all.
