# Storage and schema management

Emberdrop keeps its own data (the list of work units, their schedules and input) in a **work unit store**. The store is a pluggable provider, selected when you register Emberdrop. This page covers choosing a store, configuring it, and how its schema is created and changed.

Do not confuse the work unit store with a scheduler engine's own storage. If you use Hangfire as the scheduler, Hangfire keeps its queues and recurring entries in its own storage, configured separately. The in-memory scheduler keeps nothing on disk.

## Providers

| Provider | Package | Needs | Use for |
|---|---|---|---|
| SQLite | `Emberdrop.Storage.Sqlite` | nothing; the file is created on first run | the quick start, single-node setups |
| SQL Server | `Emberdrop.Storage.SqlServer` | a SQL Server and an **existing, empty database** | shared or production databases |
| In-memory | `Emberdrop.InMemory` | nothing | demos and tests; everything is lost on restart |

In code:

```csharp
services.AddEmberdrop(emberdrop => emberdrop
    .UseInMemoryScheduler()
    .UseSqlite("Data Source=emberdrop.db"));      // or .UseSqlServer(connectionString)
```

## Configuring the reference server

The reference server (`Emberdrop.Server`) picks providers from configuration. Defaults need no setup.

```json
{
  "Emberdrop": {
    "Scheduler": "InMemory",
    "Store": "Sqlite"
  },
  "ConnectionStrings": {
    "EmberdropConnection": "Data Source=emberdrop.db"
  }
}
```

| Setting | Values | Meaning |
|---|---|---|
| `Emberdrop:Store` | `Sqlite` (default), `SqlServer`, `InMemory` | where work unit definitions are stored |
| `Emberdrop:Scheduler` | `InMemory` (default), `Hangfire` | which scheduler engine runs the work units |
| `ConnectionStrings:EmberdropConnection` | connection string | the store's database. For SQLite a path, for SQL Server a normal connection string. |
| `ConnectionStrings:HangfireConnection` | SQL Server connection string | Hangfire's own storage. Only for `Scheduler=Hangfire`; falls back to `EmberdropConnection` when the store is SQL Server. |

The connection string key is `EmberdropConnection`. The old key `DefaultConnection` is no longer read; if you have it set in an environment variable or secrets, rename it (`ConnectionStrings__EmberdropConnection`).

### Using SQL Server

```bash
cp .env.example .env                      # set MSSQL_SA_PASSWORD
docker compose --profile sqlserver up -d  # starts SQL Server and creates the Emberdrop database

export Emberdrop__Store=SqlServer
export ConnectionStrings__EmberdropConnection="Server=localhost;Database=Emberdrop;User Id=sa;Password=<your-sql-password>;TrustServerCertificate=True;"
dotnet run --project src/Emberdrop.Server
```

Emberdrop creates tables but never creates the database itself. Against your own server, create an empty database first. To also run Hangfire on SQL Server, add `Emberdrop__Scheduler=Hangfire`.

## How the schema is managed

Each storage provider owns its schema and ships its own [FluentMigrator](https://fluentmigrator.github.io/) migrations, so the SQL for each database is written for that database:

```
Emberdrop.Storage.Sqlite     -> Migrations/CreateWorkUnitTables.cs  [Migration(1)]
Emberdrop.Storage.SqlServer  -> Migrations/CreateWorkUnitTables.cs  [Migration(1)]
```

- **Same version numbers across providers.** Version `n` means the same logical schema everywhere. A contract test checks that every relational provider produces the same columns with the same nullability.
- **Runs at startup.** The server runs every registered migrator before it starts working. Running them again is harmless.
- **A private runner.** Each provider runs its migrations in its own FluentMigrator service provider and only scans its own assembly. A host that uses FluentMigrator for its own tables is unaffected.
- **A dedicated history table.** Applied versions are recorded in `EmberdropVersionInfo`, never in FluentMigrator's default `VersionInfo`, so Emberdrop's history cannot collide with an application's.
- **Failures are loud.** A migration error stops startup. Nothing is swallowed.

### Current schema

Two tables. `WorkUnitDefinition` holds the stored work units; `WorkUnitTrigger` (below) holds the triggers an operator added. `WorkUnitDefinition` (created whole by migration 1, which is the entire migration history so far: the project had no release when the work unit vocabulary was introduced, so the initial migration was rewritten instead of adding rename migrations):

| Column | Type | Null | Notes |
|---|---|---|---|
| `WorkUnitId` | integer, identity | no | primary key |
| `Identifier` | text(500) | no | schedule key in the scheduler |
| `Name` | text(500) | no | |
| `Type` | text(500) | no | work unit type name |
| `Enabled` | boolean | no | default false |
| `Schedule` | text(50) | no | the effective cron expression: `ScheduleOverride` if set, otherwise `DefaultSchedule` |
| `Input` | text | yes | free-form, usually JSON |
| `CreatedAt` | datetime | no | **UTC** |
| `UpdatedAt` | datetime | no | **UTC** |
| `PluginId` | text(100) | yes | the plugin that provides the work unit; null for built-in work units |
| `WorkUnitName` | text(100) | yes | the logical work unit name from the plugin's manifest |
| `VersionConstraint` | text(50) | yes | `*`, `1.2.3` or `^1.2`; null means the latest |
| `InputSchemaVersion` | integer | no | schema version of `Input`; default 1 |
| `DefaultSchedule` | text(50) | yes | the plugin's suggested schedule |
| `ScheduleOverride` | text(50) | yes | the operator's schedule; wins over the default |
| `Kind` | text(50) | no | the kind of work unit; `work-unit` by default |
| `Description` | text(1000) | yes | the manifest's description |
| `Capabilities` | text | yes | the manifest's capabilities, as a JSON array (informational, see [Capabilities](concepts/capabilities.md)) |
| `InputSchema` | text | yes | JSON Schema of the input type, generated from the plugin's class when it loads |
| `OutputSchema` | text | yes | JSON Schema of the output type |

The last five columns follow the newest loaded plugin version; none of them is the operator's.

`WorkUnitTrigger` holds the triggers an operator added, apart from the triggers a plugin's manifest declares (those are not stored here; they come from the loaded plugin version). A work unit's triggers are deleted with it.

| Column | Type | Null | Notes |
|---|---|---|---|
| `TriggerId` | integer, identity | no | primary key |
| `WorkUnitId` | integer | no | the work unit it belongs to (indexed) |
| `Kind` | text(50) | no | the kind name, for example `recurring` |
| `Definition` | text(500) | no | the trigger as a token, for example `recurring:0 9 * * *` |
| `Enabled` | boolean | no | default true |
| `CreatedAt` | datetime (UTC) | no | |

All timestamps are written as UTC by every provider.

## Adding a migration

1. Add a class with the next version number to **each** relational provider's `Migrations` folder, with the same `[Migration(n)]` value:

   ```csharp
   [Migration(2)]
   public sealed class AddSomething : Migration
   {
       public override void Up() { /* ... */ }
       public override void Down() { /* ... */ }
   }
   ```

2. Update the expected columns in `RelationalStoreContract` if the shape of the table changes, and update the store if it reads or writes the new column.
3. Never edit a migration that has shipped; add a new one.

## Rolling back

Each migration has a `Down()` method, but Emberdrop does not yet provide a supported rollback command, and the FluentMigrator CLI has not been verified against the dedicated version table. Back up the database before upgrading.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `Cannot open database "Emberdrop"` on startup | The SQL Server database does not exist. Create it, or use `docker compose --profile sqlserver up -d`, which does. |
| `SQL Server connection string ... none is configured` at startup | `Emberdrop:Store=SqlServer` is set but `ConnectionStrings:EmberdropConnection` is not (a leftover `DefaultConnection` is ignored). |
| `database is locked` with SQLite | Another process holds a long write. Emberdrop enables write-ahead logging, which allows concurrent readers; keep one writer process per database file. |
| Hangfire dashboard returns 404 | The dashboard exists only with `Emberdrop:Scheduler=Hangfire`. |
