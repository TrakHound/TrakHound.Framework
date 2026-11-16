# Contributing to TrakHound Framework

Thanks for helping. Bug reports, fixes, documentation and new features are all welcome.

- **Questions and bugs:** open an [issue](https://github.com/TrakHound/TrakHound.Framework/issues/new/choose).
- **Security problems:** don't open an issue. Report them privately (see [SECURITY.md](SECURITY.md)).
- **Larger changes** (a new package, a new setting, a change to the System API or the package format): open an issue first, so we can agree on the approach before you spend time on it.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

Build projects individually. `TrakHound.Framework.sln` can reference projects outside this repository, so building the solution may fail for reasons unrelated to your change.

```bash
dotnet build instances/TrakHound.Instance/TrakHound.Instance.csproj
```

Run the test projects that cover the code you changed:

```bash
dotnet test tests/TrakHound.Common.Tests/TrakHound.Common.Tests.csproj
```

Most tests need no setup. The ones for PostgreSQL, SQL Server, TimescaleDB, InfluxDB, RabbitMQ and Redis are skipped unless their `TRAKHOUND_*_TEST_SERVER` (or `TRAKHOUND_INFLUXDB_TEST_URL`) environment variable is set. [AGENTS.md](AGENTS.md) lists every test project and those variables.

To run an Instance from source, see [Getting Started](README.md#getting-started) in the README. The [Developer Guide](docs/developer-guide.md) covers building, debugging and publishing packages.

## Conventions

[AGENTS.md](AGENTS.md) has the full list. The ones that come up most:

- **Match the surrounding code:** naming, comment style and indentation (tabs in some files, spaces in others). Comments explain why, briefly.
- **Api endpoints:** describe every endpoint and parameter, each `Description` on one line.
- **Table queries:** modules use Table Requests with the `ansi` subset, not T-SQL, so they run on every Table Service.
- **Instance routes:** a new fixed route prefix goes in `TrakHoundInstanceRoutes`. Controllers use attribute routes.
- **Configuration files:** a property that defaults to `true` (or a non-zero enum) must always be serialized, or a saved `false` reads back as the default.
- **Stylesheet:** edit the files in `libraries/TrakHound.Blazor.Hosting/Styles`, not the generated `trakhound.css`.
- **Docs:** when behavior, settings or endpoints change, update the matching guide in `docs/` (and the module's README). Write in plain, direct sentences.
- **Pre-release:** renames don't need compatibility shims for old routes, settings or properties; change every reference.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep each pull request to one change. Unrelated fixes go in their own pull request.
3. Add or update tests for what you changed, and run the affected test projects.
4. Write commit messages with a short imperative subject (ex. "Add Zones to limit what Modules can reach") and a body that explains what changed and why.
5. Open the pull request against `main` and fill in the template.

A maintainer reviews each pull request. Changes are merged by fast-forwarding `main`, so you may be asked to rebase onto the latest `main`.

## License

By contributing, you agree that your contributions are licensed under the [Apache License, Version 2.0](LICENSE), the same license as the project (section 5 of the license). You keep the copyright to your contributions.
