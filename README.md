# Emberdrop

Hot-swappable plugins for background work in .NET.

Drop a packaged plugin into a watched folder and the running host validates it, loads it in isolation, and schedules its work units. Drop a newer version and the old one finishes what it is doing, then unloads. No restart, no redeploy, no dropped work.

> **Status: v0.1, pre-release.** It works and is well tested, but the APIs and the package format can still change. See [what works and what does not](#status).

Background job libraries compile the work into the host, so changing one job means redeploying everything. Emberdrop ships each plugin as a versioned package with its own dependencies, and loads and retires it while the host keeps running.

## Read the code

The interesting part is replacing code that is running. If you want to learn from this repository, read in this order:

1. **[Hard problems](docs/engineering/hard-problems.md)**: the seven problems that make hot replacement hard, what Emberdrop does about each, and how the tests prove it.
2. **[`PluginHost`](src/Emberdrop.Hosting/)**: the host is split into partial files by concern. [Loading](src/Emberdrop.Hosting/PluginHost.Loading.cs) activates a package, [Execution](src/Emberdrop.Hosting/PluginHost.Execution.cs) takes leases on a version to run code, [Retirement](src/Emberdrop.Hosting/PluginHost.Retirement.cs) drains and cancels it, and [Unload](src/Emberdrop.Hosting/PluginHost.Unload.cs) checks that the runtime freed it. The leases and the unload check are the core of the project.
3. **[The decision records](docs/adr/)**, starting with [ADR 0001](docs/adr/0001-collectible-load-context-per-plugin-version.md) (one collectible load context per version) and [ADR 0004](docs/adr/0004-draining-and-no-forced-abort.md) (drain without aborting).
4. **[`LayeringShould.cs`](tests/Emberdrop.Architecture.Tests/LayeringShould.cs)**: an architecture test that fails the build if the hosting core starts depending on work units or schedulers.

[Architecture](docs/architecture.md) has the layer diagram.

## Status

| Works today | Not done yet | Out of scope |
| --- | --- | --- |
| Hot install, update and unload of versioned plugins | NuGet packages (plugins reference the project for now) | Sandboxing untrusted plugins: they run in the host process with its permissions |
| Draining, stuck detection and diagnostic reports | Authentication on the reference host's REST API | Forcibly stopping code that ignores cancellation |
| Quarantine and last-known-good rollback | JSON Schema validation of input (input is read strictly, not schema-validated) | |
| In-memory and Hangfire schedulers; SQLite, SQL Server and in-memory stores | Enforcing declared capabilities (they are validated and stored only) | |
| Recurring, one-off and manual triggers | CI on Windows | |
| REST API | | |

The full list of limits, stated plainly, is in [Known limits](docs/engineering/known-limits.md).

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). No database server or container is needed: by default the server keeps work units in a local SQLite file (`emberdrop.db`) and schedules them in memory.

### 1. Run the server

From the repository root:

```bash
Plugins__MonitoredPaths__0="$PWD/watched-plugins" dotnet run --project src/Emberdrop.Server
```

The server watches the folders in `Plugins:MonitoredPaths`. The default, `./watched-plugins`, is relative to `src/Emberdrop.Server`, which is where `dotnet run` sets the working directory. The variable above points it at the repository's `watched-plugins/` folder.

- API: `https://localhost:5001/api/work-units`
- Active scheduler and its features: `https://localhost:5001/api/scheduler`

### 2. Load a plugin

In a second terminal:

```bash
dotnet build SamplePlugin
```

This writes `watched-plugins/SamplePlugin.emberdrop`. The running server picks it up and registers its work units. No restart needed.

```bash
curl -k https://localhost:5001/api/plugins       # the plugin version and its lifecycle state
curl -k https://localhost:5001/api/work-units    # its work units, with status and schedule
```

### 3. (Optional) Additional providers: SQL Server and Hangfire

SQLite and the in-memory scheduler are the default path. SQL Server and Hangfire are alternative providers behind the same interfaces, and they pass the same contract tests as the defaults. Switch them with configuration. [Storage and schema management](docs/database-schema.md) lists every setting.

```bash
cp .env.example .env                       # then set MSSQL_SA_PASSWORD in .env
docker compose --profile sqlserver up -d   # starts SQL Server and creates the Emberdrop database

export Emberdrop__Store=SqlServer
export Emberdrop__Scheduler=Hangfire       # optional; adds the Hangfire dashboard at /hangfire
export ConnectionStrings__EmberdropConnection="Server=localhost;Database=Emberdrop;User Id=sa;Password=<your-sql-password>;TrustServerCertificate=True;"
dotnet run --project src/Emberdrop.Server
```

## Writing a plugin

A plugin is a .NET class library that references `Emberdrop.Abstractions`, plus a manifest. [SamplePlugin](SamplePlugin/) is a complete example.

**1. The work unit.** Implement `IWorkUnit`, usually by deriving from `WorkUnit<TInput, TOutput>`:

```csharp
using Emberdrop;

public class MyWorkUnit : WorkUnit<NoInput, NoOutput>
{
    protected override async Task<NoOutput> ExecuteAsync(NoInput input, WorkContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation("Execution {ExecutionId} started", context.ExecutionId);
        await Task.Delay(1000, cancellationToken);   // pass the token to everything that can wait
        return NoOutput.Instance;
    }
}
```

**2. The manifest.** Add an `emberdrop.json` that is copied to the build output. It names the plugin, its version, and the work units it offers. Only work units listed here can run.

```json
{
  "schemaVersion": 1,
  "id": "my-plugin",
  "version": "1.0.0",
  "entryAssemblies": [ "MyPlugin.dll" ],
  "minAbstractionsVersion": "1.0.0",
  "workUnits": [
    {
      "name": "my-work-unit",
      "type": "MyPlugin.MyWorkUnit",
      "triggers": [ { "kind": "recurring", "cron": "*/15 * * * *" } ]
    }
  ]
}
```

**3. The package.** Zip the build output into `<name>.emberdrop` and copy it into a watched folder. [SamplePlugin/SamplePlugin.csproj](SamplePlugin/SamplePlugin.csproj) has an MSBuild target that does this on every build.

To ship an update, raise `version` in the manifest ([Semantic Versioning](https://semver.org)) and drop the new package. The full guide is [Plugin system](docs/plugins.md).

## Documentation

- **Start here:** [Plugin system](docs/plugins.md) covers packages, the manifest, installing, updating and the lifecycle
- **Design:** [Hard problems](docs/engineering/hard-problems.md), [Architecture](docs/architecture.md) and the [decision records](docs/adr/)
- **Limits:** [Known limits](docs/engineering/known-limits.md)
- **Concepts:** [Vocabulary](docs/concepts/vocabulary.md), [Triggers](docs/concepts/triggers.md), [Capabilities](docs/concepts/capabilities.md)
- **Reference:** [API examples](docs/api-examples.md), [Manifest JSON Schema](docs/manifest.schema.json), [Storage and schema management](docs/database-schema.md)

## Development

```bash
dotnet restore
dotnet build
dotnet test
./scripts/run-tests-with-coverage.sh      # tests plus an HTML coverage report in .local/
```

`docker-compose.yml` defines an optional SQL Server and starts nothing by default. `docker compose --profile sqlserver up -d` starts it. It never runs the Emberdrop server.

## Security notes

- The REST API has no authentication or authorization.
- Plugins are arbitrary .NET code loaded into the server process. Only drop packages you trust into watched folders, and restrict write access to them.
- Do not expose the server to untrusted networks in its current state.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
