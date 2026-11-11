# Contributing

Thanks for your interest. Emberdrop is early-stage, so please open an issue to discuss larger changes before starting work. Participation is covered by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Setup

- .NET 10 SDK
- Nothing else to run the server: it defaults to SQLite and an in-memory scheduler. Docker is optional, for SQL Server (`docker compose --profile sqlserver up -d`, see the README) and for the SQL Server contract tests

## Before opening a pull request

```bash
dotnet restore
dotnet build
dotnet test
```

`Emberdrop.Hosting` must not learn about work units, triggers, schedulers or stores; `tests/Emberdrop.Architecture.Tests` fails if it does (see [Architecture](docs/architecture.md)).

## Guidelines

- Keep changes focused; one logical change per pull request.
- Add or update tests for behavior changes. Tests use xUnit, Moq and FluentAssertions.
- Update the docs in `docs/` and the README when behavior changes.
- Never commit secrets, connection strings or machine-specific paths.

By contributing you agree that your contributions are licensed under the MIT License.
