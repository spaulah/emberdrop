# Plugin system

A plugin is a ZIP file with the extension `.emberdrop` that contains your work unit assemblies and a manifest. Drop it into a watched folder and the running server validates it, loads it into its own isolated load context and registers its work units. No restart, no redeploy.

> This page describes what exists now. The decisions behind it are in the [decision records](adr/), and the list of what it cannot do is in [known limits](engineering/known-limits.md).

## What a package contains

```
my-plugin.emberdrop            (a ZIP file)
  emberdrop.json               the manifest (required, at the root)
  Acme.Reports.dll             your entry assembly
  Acme.Reports.deps.json       how to find your private dependencies
  SomeLibrary.dll              your private dependencies
  Emberdrop.Abstractions.dll   ignored: the host always supplies its own copy
```

Build output from a normal `dotnet build` of a class library already has this shape (plus the manifest). [SamplePlugin](../SamplePlugin/) is a working example, including the MSBuild target that creates the package.

### The manifest

`emberdrop.json` is read and validated before any of your code is loaded. A package without a valid manifest is quarantined.

```json
{
  "schemaVersion": 1,
  "id": "acme.reports",
  "version": "1.2.0",
  "description": "Nightly reports",
  "entryAssemblies": [ "Acme.Reports.dll" ],
  "minAbstractionsVersion": "1.0.0",
  "startup": "Acme.Reports.Startup",
  "workUnits": [
    {
      "name": "nightly-report",
      "type": "Acme.Reports.NightlyReportWorkUnit",
      "kind": "work-unit",
      "description": "Builds the nightly report.",
      "triggers": [ { "kind": "recurring", "cron": "0 2 * * *" } ],
      "capabilities": [ "network:smtp" ],
      "requiredConfiguration": [ "Reports:OutputPath" ],
      "inputSchemaVersion": 1
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `schemaVersion` | Must be `1`. |
| `id` | Lowercase letters, digits, `.`, `-`, `_`; up to 100 characters. Identifies the plugin across versions. |
| `version` | [Semantic Versioning](https://semver.org): `major.minor.patch`, optional `-prerelease` and `+build`. |
| `entryAssemblies` | Package-relative paths (forward slashes) of the assemblies that contain your work units. |
| `minAbstractionsVersion` | The oldest `Emberdrop.Abstractions` your plugin was built for. The package is rejected if the host's is older (major and minor are compared). |
| `workUnits[].name` | The work unit's logical name, unique within the plugin. |
| `workUnits[].type` | Full name of the class that implements `IWorkUnit`. Only work units listed here are runnable; other classes are ignored. |
| `startup` | Optional. A class with a public parameterless constructor and a method `void ConfigureServices(IServiceCollection services)` where the plugin registers the services its work units need. |
| `workUnits[].kind` | Optional. What sort of work unit this is: `work-unit` unless stated. It is a string, not a fixed list; the host rejects a kind it has not been told to run (it runs `work-unit`). |
| `workUnits[].description` | Optional. Shown in the API. |
| `workUnits[].inputType`, `workUnits[].outputType` | Optional. Full names of the input and output types. When given they must be the `TInput` and `TOutput` of the class (see [Typed input and output](#typed-input-and-output)); the host derives the JSON Schemas from the class either way. |
| `workUnits[].triggers` | Optional. The triggers the work unit declares, for example `{ "kind": "recurring", "cron": "0 2 * * *" }` or `{ "kind": "manual" }`. They are defaults: the first recurring trigger becomes the default schedule, an operator's schedule always wins and survives updates, and triggers an operator adds are stored separately. See [Triggers](concepts/triggers.md). |
| `workUnits[].capabilities` | Optional list of strings saying what the work unit needs. Validated, stored and shown by the API; **mostly informational in v0.1**. See [Capabilities](concepts/capabilities.md). |
| `workUnits[].requiredConfiguration` | Optional configuration keys the work unit needs. The plugin sees only these keys of the host's configuration. A work unit whose keys are not set is registered but not enabled, and refuses to start. |
| `workUnits[].inputSchemaVersion` | The schema version of the stored input the work unit understands (default 1). A stored work unit with a different version is refused. |

Every problem found in a manifest is reported at once, not just the first.

The manifest has a published JSON Schema: [manifest.schema.json](manifest.schema.json). Point your editor at it for completion and checking.

## Typed input and output

Derive from `WorkUnit<TInput, TOutput>` and the host does the plumbing:

```csharp
public sealed record GreetInput(string Name);
public sealed record GreetOutput(string Message);

public class GreetWorkUnit : WorkUnit<GreetInput, GreetOutput>
{
    protected override Task<GreetOutput> ExecuteAsync(GreetInput input, WorkContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new GreetOutput($"Hello, {input.Name}!"));
}
```

- **JSON Schemas.** When a package loads, the host generates the JSON Schema of `TInput` and `TOutput` (with `System.Text.Json`'s schema exporter), stores them with the work unit and returns them as `inputSchema` and `outputSchema` from the API. They are for tools and people; the host does not validate against them.
- **Strict reading.** Input is read as JSON into `TInput`, case-insensitively, and a property the type does not have is an error, not ignored. A missing, malformed or unknown-property input is refused *before* the work unit starts, with a message that names the work unit, the type and what is wrong. The API checks the same way when you create, update or start a work unit with input.
- **Output.** The returned value is written as JSON (camel case) in the execution's `WorkResult`.
- **No input or no output.** Use `NoInput` / `NoOutput`.
- **Not typed?** A class that implements `IWorkUnit` directly receives the raw JSON text in `context.Input`, and is not checked.

JSON Schema *validation* of input (as opposed to strict reading) is not done in v0.1.

## Starting an execution

`POST /api/work-units/{id}/executions` starts one execution. With no body it starts now, with a manual trigger; `runAt` starts it at that time, with a one-off trigger (the scheduler must support delayed runs); `input` replaces the stored input for this execution. The trigger is recorded with the execution; `GET /api/scheduler/executions` lists finished executions with their triggers.

```bash
curl -X POST https://localhost:5001/api/work-units/4/executions -H 'content-type: application/json' \
     -d '{ "input": { "name": "ada lovelace" } }'
```

## Writing a work unit

Reference `Emberdrop.Abstractions` (and nothing else from Emberdrop) and implement `IWorkUnit`, usually by deriving from the `WorkUnit<TInput, TOutput>` base class (use `NoInput` and `NoOutput` when there is none):

```csharp
using Emberdrop;

public class NightlyReportWorkUnit : WorkUnit<NoInput, NoOutput>
{
    protected override async Task<NoOutput> ExecuteAsync(NoInput input, WorkContext context, CancellationToken cancellationToken)
    {
        // your work; pass the token to everything that can wait
        await Task.Delay(1000, cancellationToken);
        return NoOutput.Instance;
    }
}
```

Add `emberdrop.json` to the project with `CopyToOutputDirectory`, as the sample does.

### Your own services

Each plugin version gets **its own service provider**, built the first time one of its work units runs and disposed when the version unloads. Every execution runs in its own scope, so scoped services live exactly as long as one run. Work unit classes are created by the container, so their constructors can ask for anything the container can provide.

What the plugin's container contains:

- `ILoggerFactory` and `ILogger<T>`, forwarded to the host's logging under the plugin type's name.
- `IConfiguration`, restricted to the keys the plugin's work units listed in `requiredConfiguration`.
- Whatever the host put on the allow-list (`PluginHostOptions.HostServices`). Everything else in the host is invisible to the plugin.
- Whatever the plugin's own `startup` class registers.

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ReportBuilder>();       // disposed when this plugin version unloads
        services.AddScoped<UnitOfWork>();             // one per execution
    }
}
```

If the registrations cannot be satisfied, the failure is reported with its reason instead of being left to surface at some arbitrary moment.

### Your dependencies are your own

Put a `PackageReference` in the plugin's project and set `<EnableDynamicLoading>true</EnableDynamicLoading>`: the library is then copied next to the plugin, listed in its `deps.json`, and travels in the package. [SamplePlugin](../SamplePlugin/) does this with `Humanizer.Core`, which the host does not have.

Libraries your plugin uses are private to it. Your plugin can use a different version of a library than the host or another plugin uses, and both run side by side. Only a few assemblies are shared with the host so that types can cross the boundary: `Emberdrop.Abstractions`, the `Microsoft.Extensions.*` abstractions for logging, dependency injection and configuration, and anything the application adds to the shared list. If your plugin was built against a *newer* version of a shared assembly than the host has, it is rejected with an error that names both versions. Details: [ADR 0003](adr/0003-shared-assembly-policy.md).

### Things to know

- Plugin assemblies are loaded from memory so that no file is locked ([ADR 0002](adr/0002-file-lock-free-plugin-loading.md)). `Assembly.Location` is empty; do not use it to find files.
- **Honor the `CancellationToken`.** The host signals it when your plugin version is being replaced and your run has taken too long. Code that ignores it cannot be stopped; it keeps its version loaded.
- Stop your own threads and timers when the work unit that started them is done. You can declare them with `context.Activity?.Track("thread", "what it is")` so the host can name them if the plugin refuses to unload.
- A work unit that throws surfaces to the scheduler as a `PluginExecutionException` that carries the original exception as text. This is deliberate: the original exception object would keep your plugin's code in memory for as long as anyone held it.
- Do not subscribe to static events (of the host or the runtime) without unsubscribing. See [known limits](engineering/known-limits.md).

## Stored work units, versions and schedules

Each plugin work unit is registered as a stored work unit when its plugin loads. A stored work unit is bound to a **plugin id**, a **logical work unit name** (from the manifest, never a class name) and a **version constraint**:

| Constraint | Runs on |
|---|---|
| `*` (or empty) | the latest loaded version |
| `1.2.3` | exactly that version (a pin) |
| `^1`, `^1.2`, `^1.2.3` | the latest loaded version with the same major version, at least that |

A new work unit follows its plugin within the major version it was registered under (`^1.0.0`); moving to the next major version is the operator's decision (`PUT /api/work-units/{id}` with `versionConstraint`). Details and the alternatives considered: [ADR 0005](adr/0005-version-binding-for-persisted-work-units.md).

**Status.** `GET /api/work-units` shows a `status` and a `statusReason` for every work unit:

- `Ready`: it can run now.
- `WaitingForPlugin`: no loaded version matches. The reason says why (`No loaded version of plugin 'acme' satisfies ^1.0.0; loaded: 2.0.0.`). Its schedule is paused, and the missed run is repeated as soon as a matching version loads. (Set `WorkUnitOptions.MissingPlugin` (`ConfigureWorkUnits`) to `Fail` to make such a run fail instead.)
- `MissingConfiguration`: a matching version is loaded but a key it requires is not set.
- `InputSchemaMismatch`: the stored input were written for a different schema version than the plugin expects. The work unit does not start. Update the input and `inputSchemaVersion`, or pin the work unit to a version that expects the stored one.

**Schedules.** The plugin's `defaultSchedule` is a default. When you change a work unit's schedule through the API it is recorded as an *override*, which a plugin update never replaces; setting it back to the default clears the override. `enabled`, the version constraint and the input are likewise yours. When a plugin's last active version goes away, its work units are paused (taken out of the scheduler, kept in the store) and resume when it returns. Nothing is deleted. Rules and rationale: [ADR 0006](adr/0006-schedule-reconciliation.md).

## Installing and updating

Packages are picked up from the folders listed under `Plugins:MonitoredPaths` (default `./watched-plugins`) while the server runs, and once at startup.

```json
{
  "Plugins": {
    "MonitoredPaths": [ "./watched-plugins" ],
    "EnableFileWatcher": true,
    "StorageRoot": ".emberdrop/plugins",
    "ArchiveRetention": 3,
    "AllowedPackageHashes": []
  }
}
```

### What happens to a package

1. **Wait until the file is complete.** A file event means a file *started* to exist, not that its copy has finished. The installer waits until the file's size and modification time have held still across consecutive checks **and** the file can be opened exclusively, which fails while a writer still holds it. A file that never settles is left alone (it is not quarantined) and picked up again by a later event. The several events one copy produces are merged into one piece of work.
2. **Check the hash**, if you configured an allowlist (below).
3. **Extract into a staging directory** (`<StorageRoot>/staging/<id>`), never straight into the final place. Extraction rejects, before writing anything: an entry whose name is absolute, contains `..`, contains a backslash, or would land outside the staging directory (zip-slip); symbolic links; duplicate names; more than 5,000 entries; more than 500 MB when extracted; and anything that expands to more than 100 times the size of the package file (the signature of a zip bomb). The bytes actually written are counted as they stream out, so an archive whose headers lie about sizes does not get past the limits either. All limits are options on `PluginHostOptions`.
4. **Read and validate the manifest**, and prove the package would activate: its entry assemblies load and run their module initializers, its work units exist, the shared-assembly rules hold, and its services can be built.
5. **Promote** the staging directory to `<StorageRoot>/installed/<id>/<version>/` with a single rename, so an install is never half done. If the server stops in the middle, the leftovers in `staging/` are removed at the next start.
6. **Activate** it and register its work units. The original package is kept in the version's folder next to a `package.sha256` file.

**If any step fails, whatever was running keeps running.** The package file is moved to `<StorageRoot>/quarantine/` with a `<name>.reason.json` beside it that lists every problem, and the version appears in `GET /api/plugins` as `Quarantined` with the reason.

- A *different* version is activated beside the old one, so a failure cannot touch the old one.
- A replacement for the *same* id and version is first loaded into a throwaway context to prove it works, and only then is the installed copy retired. If it cannot activate, the installed copy is never touched.
- Dropping the exact same package again does nothing.

```
<StorageRoot>/
  installed/<id>/<version>/           loaded versions (with package.emberdrop and package.sha256)
  archive/<id>/<version>_<time>/      replaced versions; only the newest ArchiveRetention are kept
  quarantine/                         rejected packages, each with <name>.reason.json
  staging/                            transient
```

### Integrity: an optional hash allowlist

`Plugins:AllowedPackageHashes` takes SHA-256 hashes (hex, any case) of the package files you allow. **Empty, the default, means no check.** With entries, a package whose hash is not listed is quarantined, and the reason names the hash so you can add it once you have decided to trust the package. The list is checked again at startup against the hash recorded when each package was installed.

```bash
shasum -a 256 my-plugin.emberdrop      # or: sha256sum
```

This limits **which** package may load. It is not code signing and says nothing about what a trusted package does once loaded; see [known limits](engineering/known-limits.md).

### Updating a plugin

Drop a package with a new version. The new version becomes active at once and takes all new work. The older version is **retired**: it stops receiving new executions, the ones already running get a grace period to finish (default 30 seconds), they are then asked to stop through their `CancellationToken` (default another 30 seconds), and only when none are left does the old version's code get unloaded and its directory archived. If something still will not stop, the version stays in the `Draining` state and is flagged *stuck*; the host forces nothing (see [ADR 0004](adr/0004-draining-and-no-forced-abort.md)). The drain timeouts and the stuck policy are options on `PluginHostOptions`.

Dropping a package with the *same* id and version replaces the installed one, after proving the replacement works and retiring the installed copy the same way. If the installed copy is stuck, the new package is set aside instead and the installed copy stays in service. Set `PluginHostOptions.RetireReplacedVersions` to `false` to keep older versions active beside the new one, for example when stored work units are pinned to them.

### Lifecycle

Each version moves through these states; the current one is part of the API.

```
Staged -> Validated -> Active -> Draining -> Unloading -> Unloaded
   \          \                                    
    +--> Failed --> Quarantined          (Quarantined can also come straight from Staged or Validated)
```

### Seeing what is loaded

- `GET /api/work-units/types` lists every work unit that can currently run and whether it comes from a plugin.
- `GET /api/plugins` lists every plugin version the host knows about with its state, how many executions are running on it, whether it is stuck, and, for an unloaded version, whether the runtime really freed its code (`contextAlive`).
- `GET /api/plugins/{id}/{version}/report` is the diagnostic report: the executions still running on that version (including abandoned ones), anything the plugin declared through `IPluginActivity`, the usual reasons a plugin will not unload, and how to take a heap dump. What the report can and cannot tell you is in [known limits](engineering/known-limits.md).
