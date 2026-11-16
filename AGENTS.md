# TrakHound Framework

A modular .NET 10 platform: a TrakHound **Instance** (ASP.NET Core / Blazor host) loads **packages** (Apis, Apps, Functions, Workers, Services, Identity providers) and serves them, with an Admin interface, a System API and an MCP Server. See [README.md](README.md) for the concepts and the [docs](docs) for the guides.

## Layout

| Folder | What's there |
| --- | --- |
| `instances/TrakHound.Instance` | The Instance: startup and request pipeline (`TrakHoundInstanceHost.cs`, with its middleware in `Http/Pipeline`), System API controllers (`Http/Controllers`), security (`Security`), MCP Server (`Mcp`), Admin interface pages (`Pages`) and the components only they use (`Components`) |
| `libraries/TrakHound.Common` | Everything packages and the Instance share: package base classes, `ITrakHoundClient`, Api attributes and the OpenAPI builder, configurations, security |
| `libraries/TrakHound.AspNetCore` | ASP.NET Core code the Instance and the Api debug host share (the WebSocket and Server-Sent Events stream handling) |
| `libraries/TrakHound.Hosting*` | Running modules standalone (a console app, Windows Service, IIS or cloud app): `AddTrakHound` and the hosted Workers, `TrakHoundModuleFactory` (creates modules the way the Instance does) and, in `TrakHound.Hosting.AspNetCore`, `MapTrakHoundApi` (maps an Api's endpoints and streams). The debug hosts and `TrakHound.Testing` are built on them |
| `libraries/TrakHound.Blazor*` | App base classes and UI components |
| `modules/` | First-party packages (ex. `TrakHound.Functions.Api`, `TrakHound.SqlServer.Service`) |
| `cli/` | The TrakHound CLI (`th`): packs, publishes and installs packages |
| `templates/` | `dotnet new` templates for packages (each project gets its own `AGENTS.md`) |
| `debug/`, `examples/` | Debug hosts and the `TrakHound.Testing` unit test library for running packages without an Instance, and example packages |
| `tests/` | xUnit test projects |
| `docs/` | Installation, User and Developer guides (`.md`, with `.pdf` copies) |

## Build and test

- Build projects individually, ex. `dotnet build instances/TrakHound.Instance/TrakHound.Instance.csproj`. `TrakHound.Framework.sln` can reference projects outside this repository, so building the solution may fail for reasons unrelated to your change.
- Tests: `dotnet test tests/<Project>/<Project>.csproj` (`TrakHound.CLI.Tests`, `TrakHound.Common.Tests`, `TrakHound.Debug.AspNetCore.Tests`, `TrakHound.Functions.Api.Tests`, `TrakHound.Hosting.Tests`, `TrakHound.Instance.Deployments.Tests`, `TrakHound.Instance.Http.Tests`, `TrakHound.Instance.Routing.Tests`, `TrakHound.Instance.Runtime.Tests`, `TrakHound.Instance.Zones.Tests`, `TrakHound.MessageQueues.Conformance.Tests`, `TrakHound.Mqtt.Service.Tests`, `TrakHound.Packages.Api.Tests`, `TrakHound.RabbitMq.Service.Tests`, `TrakHound.Redis.Service.Tests`, `TrakHound.ScheduledTasks.Api.Tests`, `TrakHound.ScheduledTasks.Worker.Tests`, `TrakHound.SqlServer.Service.Tests`, `TrakHound.Tables.Conformance.Tests`, `TrakHound.Testing.Tests`, `TrakHound.TimeSeries.Conformance.Tests`). Run the ones covering the code you changed. `TrakHound.Tables.Conformance.Tests` runs the Table Services and the modules that use Tables on SQLite without any setup, and on PostgreSQL and SQL Server when `TRAKHOUND_POSTGRES_TEST_SERVER` or `TRAKHOUND_SQLSERVER_TEST_SERVER` is set. `TrakHound.TimeSeries.Conformance.Tests` runs every Time Series Service against the same tests: on the memory Service without any setup on TimescaleDB when `TRAKHOUND_TIMESCALE_TEST_SERVER` is set, and on InfluxDB when `TRAKHOUND_INFLUXDB_TEST_URL` is set. Each new Service adds a class to its `ProviderTests.cs`. `TrakHound.MessageQueues.Conformance.Tests` runs every Message Queue Service against the same tests: on the memory Service without any setup, and on RabbitMQ when `TRAKHOUND_RABBITMQ_TEST_SERVER` is set. `TrakHound.Redis.Service.Tests` runs against Redis when `TRAKHOUND_REDIS_TEST_SERVER` is set, and `TrakHound.RabbitMq.Service.Tests` against RabbitMQ when `TRAKHOUND_RABBITMQ_TEST_SERVER` is set.
- Modules reference `debug/TrakHound.Debug.*`, which reference `TrakHound.Common` as a project, so building a module also builds the libraries.
- The TrakHound stylesheet (`wwwroot/css/trakhound/trakhound.css` in `TrakHound.Blazor.Hosting`) is generated on build by joining the files in `libraries/TrakHound.Blazor.Hosting/Styles`, in the order listed in its `.csproj`. Edit those files (and add new ones to that list), not the generated file. The themes (`trakhound-light.css`, `trakhound-dark.css`) are separate files in `wwwroot`.
- `.claude/launch.json` runs the Instance from its Debug build output (`TRAKHOUND_CONFIGURATION_PROFILE` picks the configuration profile under `config/`). Stop it before building: it locks the build output.
- The Admin interface can't create its first admin account. For a debug profile without one, run `instances/TrakHound.Instance/bin/Debug/net10.0/TrakHound.Instance.exe set-admin <username> --profile <profile>` (it asks for the password). It creates an Owner, kept in that profile's `security/admins`, so it's only needed once per build output folder. More accounts (with other Admin Roles) are added in Security > Admin Accounts.

## Conventions

- **Pre-release:** the project is pre-release (see `Directory.Build.props`). Renames don't need compatibility shims for old routes, configuration keys or properties; change every reference.
- **Instance routes:** a new fixed route prefix must be added to `TrakHoundInstanceRoutes` (and allowed by `TrakHoundInstanceRequests.IsValidRoute`), or requests get 404 before routing. Controllers use attribute routes (`[HttpGet("_logs/...")]`), never `MapControllerRoute`: conventional routes are matched after the App Host's catch-all page, so they would always 404 (a test checks this). System API areas are security resources in `TrakHoundSystemSecurity`.
- **Admin pages:** every Admin interface page declares the Admin Resource it belongs to with `@attribute [AdminResource(...)]` (or in its folder's `_Imports.razor`), using the resources in `TrakHoundAdminSecurity` (a test checks this). Code that makes a change from the Admin interface calls `TrakHoundAdminContext.DemandWrite(resource)` (or returns `GetDenial`), so the change is refused for an Admin whose Roles don't allow it even if a page doesn't hide the button. Give a button (or button-styled link) that changes something the `admin-write` class: the layout hides those on a page the Admin can view but not change.
- **Admin areas:** every Admin interface area (Explorer, Packages, Configuration, Deployments, Security, Licenses) is laid out by `AreaLayout` in `Components`: one header row (`AreaHeader`), a sidebar of the area's sections (`AreaNavGroup`, `AreaNavItem`) and the page. Don't give an area or a page its own title header, tab bar or breadcrumbs. A section is a sidebar item with its own route (ex. `_admin/packages/browse`), not a `?tab=` query or a tab. Tabs are only for the views of one item inside a page (ex. a Function's Information, Run and History). A page sets its trail with `<AreaBreadcrumbs Items="..." />`, ending with the page itself; an area's first page sets none, so the header shows the area's description. Tools that apply to the whole area go in the layout's `Tools` (ex. the Package Source). The Explorer keeps its tree sidebar and uses `AreaHeader` directly; its pages set `ExplorerService.BreadcrumbItems`.
- **Configuration files:** a property that defaults to `true` (or a non-zero enum) must always be serialized (`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`, no YAML `OmitDefaults`), or a saved `false` reads back as the default.
- **Api endpoints:** describe every endpoint and parameter. Keep each `Description` on one line (no `+` concatenation) and put each described parameter on its own line:

  ```csharp
  [ApiGet("runs", Description = "Lists the Runs that started between start and stop.")]
  public async Task<TrakHoundApiResponse> GetRuns(
      [FromQuery(Description = "The earliest Run start to include")] string start,
      [FromQuery(Description = "The most Runs to return (up to 10,000)")] int take = 100
      )
  ```

  Mark endpoints that are safe for AI agents with `[ApiMcpTool]` (Developer Guide, section 4.1).
- **TrakHound.Client:** the [TrakHound.Client](https://github.com/TrakHound/TrakHound.Client) repository copies the client interfaces, the HTTP clients and their models from `libraries/TrakHound.Common` (the list is its `sync/sync-files.txt`) and also builds them for .NET Standard 2.0. Keep those files free of module, package, Service Router, Security and Zone code and of default interface members (put factories that need them in Common, ex. `TrakHoundApiInformationFactory`), and use `#if NET` only where a shim in that repository can't work. After changing one, run that repository's `sync/sync.ps1` and its contract tests.
- **Table queries:** modules use Table Requests with the `ansi` subset, not T-SQL or `ExecuteQuery(string)`, so they run on every Table Service (Developer Guide, section 5).
- **Docs:** update the matching guide in `docs/` (and module READMEs) when behavior, settings or endpoints change. Write in plain, direct sentences.
- **Style:** match the surrounding code (naming, comment style, indentation, which is tabs in some files and spaces in others). Comments explain why, briefly.

## Git

- Commit on a branch; `main` is updated by fast-forwarding it to finished branches. Don't push unless asked.
- Commit messages: a short imperative subject (ex. "Add Zones to limit what Modules can reach") and a body that explains what changed and why.
