# TrakHound.Framework

TrakHound Framework is a modular .NET 10 platform for building, deploying, and hosting **APIs, Blazor Apps, Functions, Workers, Services, and Identity providers** as installable packages. A TrakHound **Instance** hosts these packages, routes requests to them, and provides a web-based admin UI for configuration, exploration, and deployment.

## Documentation

| Guide | For |
| --- | --- |
| [Installation Guide](docs/instance-installation-guide.md) ([PDF](docs/instance-installation-guide.pdf)) | Installing, upgrading and uninstalling an Instance as a Windows Service, in IIS, or as a systemd service on Linux |
| [User Guide](docs/instance-user-guide.md) ([PDF](docs/instance-user-guide.pdf)) | Using the Admin interface and configuring a running Instance |
| [Developer Guide](docs/developer-guide.md) ([PDF](docs/developer-guide.pdf)) | Building, debugging and publishing packages |

## Concepts

| Term | Description |
| --- | --- |
| **Instance** | The ASP.NET Core / Blazor host process that loads packages, exposes HTTP endpoints, and serves the admin UI. |
| **Package** | A deployable unit described by a `trakhound.package.json` manifest. Each package has a `category`: `api`, `app`, `function`, `worker`, `service`, or `identity`. |
| **Api** | A controller-style HTTP API (`TrakHoundApiController`) mounted at a configurable route. Supports GET/PUT/POST/DELETE and streaming responses (WebSocket or Server-Sent Events). Endpoints can also be served as MCP Tools. |
| **App** | A Blazor UI application hosted within the Instance. |
| **Function** | A unit of code that is run on demand or on a schedule. A **Job** is a named set of Parameters for a Function, and a **Scheduled Task** runs a Function (optionally with a Job) on a CRON schedule. |
| **Worker** | A long-running background process. |
| **Service** | A backing provider for data primitives — Blobs, Key-Values, Messages, Message Queues, Tables, and Time Series. Many Messages can be published over one WebSocket with a publish stream (`/_services/messages/publish/stream`, or `PublishStream` on the Client). |
| **Router** | Sends Service calls to Services (or other Routers). Each Route tries its Targets in order and falls back to the next one only on the results that Target lists (by default `NotFound`, `NotAvailable`, `Timeout` and `InternalError`). Edited on the admin UI's Routers diagram. |
| **Identity** | An authentication provider (token, OpenID Connect, LDAP, or the built-in API keys and users) used to secure Apis, Apps, and Service operations. |
| **Volume** | A scoped storage area provided to packages for files and configuration. |
| **Group** | Organizes Apis, Apps, Functions and Workers (ex. the Modules of one line) and sets the Router of those that don't name one. Groups never restrict anything; a Zone can list a Group (`group:<id>`) to restrict every Module in it. |
| **Zone** | A group of Modules (Apis, Apps, Functions, Workers, Services) whose Client calls are limited to what the Zone's Rules allow. |
| **Configuration Variable** | A value shared by every configuration in a profile, referenced from parameter values as `${name}` (or `${env:NAME}` for an environment variable). |

## Repository Layout

```
cli/         TrakHound.CLI (`th`) – create, pack, publish and install packages and deployments
debug/       Debug hosts for running Api, App, Function, Worker, Service and Identity packages outside of an Instance, and TrakHound.Testing for unit tests
examples/    Example packages (TrakHound.Example.*: an Api, App, Function and Worker that work together)
extensions/  TrakHound.VisualStudio – Visual Studio extension to publish packages through the CLI
instances/   TrakHound.Instance – the host application and admin UI
libraries/   Core libraries shared by the Instance and packages
modules/     First-party packages (APIs, Services, Functions, Workers, Identity)
templates/   `dotnet new` project templates for creating new packages
tests/       xUnit test projects (the libraries, CLI, modules, the Instance, and conformance tests for every Table, Time Series and Message Queue Service), and k6 load tests in tests/load
docs/        Installation, User and Developer guides (Markdown, with PDF copies)
```

[AGENTS.md](AGENTS.md) describes the repository for AI coding tools (layout, building and testing, conventions), and [CLAUDE.md](CLAUDE.md) imports it for Claude Code.

### Libraries

| Project | Description |
| --- | --- |
| `TrakHound.Common` | Core abstractions: Api, Apps, Clients, Functions, Workers, Services, Packages, Volumes, Variables, Security, Logging. |
| `TrakHound.AspNetCore` | ASP.NET Core code shared by the Instance and the Api debug host (WebSocket and Server-Sent Events streams). |
| `TrakHound.Hosting` | Runs Workers, Functions and Services outside of an Instance (`AddTrakHound`, `AddTrakHoundWorker<T>`, `RunTrakHoundFunction<T>`), and `TrakHoundModuleFactory`, which creates modules the same way the Instance does. |
| `TrakHound.Hosting.AspNetCore` | Runs an Api in an ASP.NET Core app (`MapTrakHoundApi`, `MapTrakHoundOpenApi`, API key authentication), and a Function over HTTP. |
| `TrakHound.Blazor` | Shared Blazor components, layouts, and routing. |
| `TrakHound.Blazor.Hosting` | Static assets (CSS, JS) for hosting TrakHound Blazor apps. |
| `TrakHound.Blazor.Diagrams` | Blazor diagramming components (used for the Routing editor and the Zone Map). A copy of [Z.Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams), see its [README](libraries/TrakHound.Blazor.Diagrams/README.md). |

The `debug/` projects are NuGet packages too: `TrakHound.Debug`, `TrakHound.Debug.AspNetCore` and `TrakHound.Debug.Apps` run a package locally without an Instance, and `TrakHound.Testing` runs its modules in unit tests. See the [Developer Guide](docs/developer-guide.md) (section 10).

Official builds use Font Awesome Pro icons. Those files are licensed and aren't in this repository, so a build from source shows a generic outline wherever an icon would be. Everything else works the same, and buttons without text have a tooltip. To use your own Font Awesome Pro license, put its files in `libraries/TrakHound.Blazor.Hosting/wwwroot/css/font-awesome` (ignored by git). They're used when you run or publish from source, but never packed: the TrakHound.Blazor.Hosting NuGet package and TrakHound packages always leave them out, and a pack that would include them fails.

### Modules

| Package | Category | Description |
| --- | --- | --- |
| `TrakHound.Packages.Api` | api | Download, upload, and manage packages |
| `TrakHound.Packages.App` | app | UI for managing packages |
| `TrakHound.ApiExplorer.App` | app | Browse the Apis you can use, try them out, and download their OpenAPI documents |
| `TrakHound.Welcome.App` | app | A home page (at `/`) for development Instances: what's installed, where it's served, and how to get started |
| `TrakHound.Packages.Retention.Function` | function | Delete older packages |
| `TrakHound.Functions.Api` | api | View, run and manage Functions, Jobs and Scheduled Tasks |
| `TrakHound.Functions.Retention.Function` | function | Function retention cleanup |
| `TrakHound.ScheduledTasks.Api` | api | Manage scheduled tasks that run Functions on a CRON schedule |
| `TrakHound.ScheduledTasks.Worker` | worker | Runs Functions (with their Job's Parameters) on a CRON schedule and reports each Task's status |
| `TrakHound.FileSystem.Service` | service | File system storage for Blobs |
| `TrakHound.Memory.Service` | service | In-memory Key-Value (with a configurable TTL), Message and Message Queue services, for development and tests |
| `TrakHound.Http.Service` | service | Route services to another TrakHound Instance over HTTP |
| `TrakHound.Mqtt.Service` | service | MQTT services |
| `TrakHound.RabbitMq.Service` | service | RabbitMQ services |
| `TrakHound.Redis.Service` | service | Redis services |
| `TrakHound.SqlServer.Service` | service | SQL Server services |
| `TrakHound.Postgres.Service` | service | PostgreSQL services |
| `TrakHound.Timescale.Service` | service | TimescaleDB Time Series services |
| `TrakHound.InfluxDb.Service` | service | InfluxDB 3 Time Series services |
| `TrakHound.Sqlite.Service` | service | SQLite services |
| `TrakHound.Token.Identity` | identity | Authentication via a configurable static token |
| `TrakHound.Oidc.Identity` | identity | Sign in with an OpenID Connect server (ex. Microsoft Entra ID, Okta, Keycloak) and accept its Bearer tokens |
| `TrakHound.Ldap.Identity` | identity | Sign in with a username and password verified by an LDAP server (ex. Active Directory) |

### Examples

Example packages that work together around a simulated sensor. Install `TrakHound.Example.App` to install and configure all four. See [examples](examples/README.md).

| Package | Category | Description |
| --- | --- | --- |
| `TrakHound.Example.Worker` | worker | Reads a simulated sensor and writes the recent readings to a shared Volume |
| `TrakHound.Example.Function` | function | Summarizes the recent readings (count, minimum, maximum, average) |
| `TrakHound.Example.Api` | api | Serves the readings (with a stream), runs the Function, and reads and writes Variables (route `example/api`) |
| `TrakHound.Example.App` | app | A page that shows the readings from the Api and the Function's summary (route `example`) |

## Getting Started

### Install an Instance

The Windows installer (`trakhound-instance-<version>-install.exe`) installs the Instance as a Windows Service (served on port **8472** by default) or into an IIS Application. On Linux, the release archive (`trakhound-instance-<version>-linux-x64.tar.gz` or `-linux-arm64`) includes the .NET runtime and an `install.sh` that installs, upgrades or uninstalls the Instance as a systemd service. See the [Installation Guide](docs/instance-installation-guide.md) for requirements, upgrades, silent installs and uninstalling.

Behind a reverse proxy (ex. nginx serving HTTPS), add the proxy's address to **Trusted Proxies** (**Settings > Interfaces**, `trustedProxies` in `instance.config.yaml`). The Instance then takes the client's address from `X-Forwarded-For` and the scheme from `X-Forwarded-Proto`, so logs, Rate Limits and the Module Token check see the real client instead of the proxy. See the Installation Guide, section 5.3.

The rest of this section covers building and running from source.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
dotnet build instances/TrakHound.Instance
```

The target framework and version are set in [Directory.Build.props](Directory.Build.props). Builds are versioned `[VersionPrefix].[Revision]`, where the Revision is the number of commits since `VersionPrefix` last changed, so every machine builds the same version for a commit.

The release Libraries, `dotnet new` Templates, installers and Docker images are built and published from a commit of this repository by separate release tooling, and each release commit is tagged `v[version]`.

### Run an Instance

```bash
dotnet run --project instances/TrakHound.Instance
```

The Instance accepts an optional configuration profile ID and the options `--configurationPath`, `--packagePath`, `--volumePath`, and `--deploymentPath` (see [Deployments](#deployments)). Once it's running, open the admin UI in a browser to configure Apis, Apps, Functions, Workers, Services, Routers, and Identity providers.

The admin UI can't create its first admin account. For a new configuration profile, create it with the `set-admin` command (it asks for the password), then sign in. That account is an Owner and can add more accounts with other roles in Security > Admin Accounts:

```bash
dotnet run --project instances/TrakHound.Instance -- set-admin <username> --profile <profile>
```

See the [User Guide](docs/instance-user-guide.md) (section 3.2) for the other ways to create or reset the account.

## Functions and Scheduling

Functions are managed in the admin UI's Explorer:

- **Run** – run a Function manually with Parameters entered directly or loaded from a Job.
- **Jobs** – named sets of Parameters for a Function. Edits to a Job apply to its next Run, and a Job used by a Scheduled Task can't be deleted.
- **Schedule** – add and edit Scheduled Tasks (description, CRON schedule, time zone, run missed, and Job).
- **Schedule Board** – schedule Functions and Jobs by dragging them into a column per schedule; move cards between schedules, pause, resume or delete them, with Undo. The Board also shows the Scheduled Tasks Worker's status (running now, waiting, upcoming, and recent events).
- **Summary** – run metrics per Function and per Scheduled Task (next run, missed runs, and a run timeline).
- **Export / Import** – copy Jobs and Scheduled Tasks between Instances from the Schedule Board or a Function's Schedule tab (imports are checked with a dry run first). Job IDs are readable slugs that are the same on every Instance, so a copied Task still finds its Job.

## Configuration Variables

A parameter value in any configuration can reference a variable shared by every configuration in the profile as `${name}` (ex. `directory: ${dataPath}/files`), or an environment variable of the Instance process as `${env:NAME}`. `$${name}` is the literal text `${name}`.

Variables are stored in `config/<profile>/variables.config.yaml`, edited under **Configuration > Variables** (which lists usages and undefined references), and included in Deployments. Variables with secret names are encrypted. They are read when the Instance starts, so restart it to apply a change.

In a package, `GetParameter()` returns values with references replaced, and `GetConfigurationVariable("dataPath")` (Apis, Apps, Functions and Workers) reads a variable by name.

## Security

Resources (Apis, Apps, and Service operations) are protected by assigning permissions to roles on the admin UI's **Security** page. Identity providers authenticate requests and interactive sign-ins:

- **Sign in** – interactive providers (OIDC, LDAP, and the built-in `users` provider) sign users in through `/_identity/signin` and are issued an encrypted cookie session. Repeated wrong passwords lock sign in for a while (per client address and per username).
- **API keys** – the built-in `api-keys` provider accepts hashed keys sent as a `Bearer` token or an `X-API-Key` header. Keys are managed on the **Security > API Keys** tab.
- **Users** – the built-in `users` provider signs in with a username and password stored (as a PBKDF2 hash) on the Instance. Users are managed on the **Security > Users** tab. It's meant for Apps with low security needs or a few users; use OIDC or LDAP for anything more.
- **Service operations** – each operation is a resource named `service:{serviceId}:{family}:{operation}` requiring `read` or `write`. Calls made outside of a request (Workers, Functions) are trusted.
- **Unprotected resources** – Apis and Apps that no Profile covers are public. System Resources and Service operations that no Profile covers can only be used from the same machine (other machines get `403`), unless **Settings > Interfaces > Unprotected Remote Access** is on. Each System API Controller must declare its Resource, or its requests are denied, and its changes are only made by `POST`, `PUT` or `DELETE`.

### Rate Limits

Rate Limits stop one caller (a script polling too fast, or an AI client in a loop) from slowing down the Instance, or the databases behind its Apis, for everyone else. They limit the Apis and the System API over HTTP, and the MCP Server's Tools, each with a number of requests per window and a number of concurrent requests. Callers are counted by Identity (ex. an API key), or by IP Address when anonymous.

- Requests over a limit get `429 Too Many Requests` with `Retry-After`. MCP Tool calls get a Tool error instead, which an AI client can act on.
- Calls made inside the Instance (Functions, Workers, Apps, Module Tokens), the Admin interface, Apps and health checks are not limited, and WebSocket and Server-Sent Events streams don't count toward concurrency.
- Off by default. Turn them on in **Settings > Rate Limits** (`rateLimits` in `instance.config.yaml`); saved changes apply to the next request. See the [User Guide](docs/instance-user-guide.md) (section 3.5).

Rate Limits protect against mistakes, not attacks: use a reverse proxy or gateway to block heavy traffic from the internet.

Separately, request bodies are limited to 256 MB by default (`maxRequestBodySizeMb`, **Settings > Interfaces > Max Request Size**, 0 = no limit). Larger requests get `413 Payload Too Large`, and a saved change applies to the next request.

### Zones

A Zone (**Config > Zones**) lists Member Modules and the Rules for what they can reach through their Client (Apis, Apps, Functions, Workers, Volumes, Instances and Service operations). Anything a Rule doesn't allow is denied (**Enforce**) or only recorded as "Would Deny" (**Audit**). A Zone can also limit which Modules call its Members (Allowed Callers) and send every Service call its Members make through a Router (ex. to test data). Modules not in a Zone are not restricted. A Zone can list a Group (`group:<id>`) as a Member or Allowed Caller to cover every Module in it.

Denied calls are recorded as Activity that can be granted in one click, and Members whose Packages use the file system, network or processes directly are flagged. **Config > Zone Map** draws the Zones as a diagram, with denied calls as they happen; drag Modules onto a Zone to add or move them.

Zones guard against accidental access (ex. Modules in development or written with AI coding tools). They are not a boundary against deliberately hostile code.

A package that runs code in its own process (ex. a Python or Node.js worker) can give it a **Module Token** (`Client.CreateModuleToken()`), passed as `TRAKHOUND_URL`, `TRAKHOUND_MODULE_TOKEN` and `TRAKHOUND_ROUTER_ID`. The process's calls to the System API and Apis are then made as the package, so its Zone still applies. Tokens are only accepted from the same machine and stop working when the Instance restarts.

## MCP Server

The Instance can serve an MCP (Model Context Protocol) Server at `/_mcp` so AI clients can list and run Functions, read Logs, query Tables, read and set Key Values, read and write Time Series, publish and wait for Messages, read Volume files, and describe the Apis. It's off by default: turn it on in **Settings > Interfaces > MCP Server** (the System API must be on too), then connect a client, for example:

```bash
claude mcp add --transport http trakhound http://localhost:8472/_mcp --header "X-API-Key: thk_..."
```

Like the rest of the System API, access is restricted by Identity Profiles. Without one it can only be used from the same machine (see Security), so add one before connecting from other machines: `read` on `system:mcp` connects and allows the read-only Tools, `write` is also needed for Tools that change something, and each Tool needs the Permission for the Resource it uses. Clients only see the Tools the caller can use.

Api Endpoints can be Tools too. Each Api's **MCP Tools** setting (`mcpTools`) is `Off`, `Marked` (default; Endpoints marked with `[ApiMcpTool]`) or `All`, and **Read-only Tools only** (`mcpReadOnly`, on by default) serves only the Tools that don't change anything. `mcpInclude` / `mcpExclude` route patterns (ex. `GET /orders/*`) narrow them further.

The server also offers Resources (the caller's `llms.txt` as `trakhound://llms.txt`, and each Api's OpenAPI document as `trakhound://apis/<apiId>/openapi`) and Prompts that chain the Tools (`instance_overview`, `diagnose_function`, `explore_api`, `query_tables`). See the [User Guide](docs/instance-user-guide.md) (section 2.10) for details.

## Api Catalog and API Explorer

Developers without access to the admin UI can discover Apis on the HTTP Port:

- `/_developer/catalog` lists the Apis with only the Endpoints the caller's Identity (session, API key or Bearer token) is allowed to call. It's protected by the `system:api-catalog` Resource.
- `/_developer/openapi/{apiId}` returns the OpenAPI document filtered the same way.
- `/llms.txt` ([llmstxt.org](https://llmstxt.org)) is a Markdown index of the Instance for AI agents: how to connect (the MCP Server and API keys) and each Api with its Endpoints and OpenAPI link. It's generated per caller from the Catalog and protected the same way.
- `TrakHound.ApiExplorer.App` browses the Catalog, sends "Try It" requests with the developer's own credentials, and shows code samples (cURL, JavaScript, C#, Python).
- `TrakHound.Welcome.App` is a home page for development Instances (default route `/`). It shows the Instance, its Apps, Apis, Functions, Services and packages, links to the Admin Interface, API Explorer, `llms.txt` and MCP Server, and the template commands for the Instance. It reads the System API from the visitor's browser, so visitors only see the areas they can read. Don't install it in production. See [its README](modules/TrakHound.Welcome.App/README.md).

The System API is described by OpenAPI documents as well (on the Admin Port when there is one, protected by `system:instance`): `/_instances/openapi` for all of it, `/_instances/openapi/areas` for its areas, and `/_instances/openapi/{area}` for one area. Each operation lists the System resource and permission it needs.

## Deployments

A Deployment bundles Packages and Configurations to install on another Instance. It can include only some Configuration Categories (ex. Apis, Functions, Services, Instance Settings) and no Packages, to copy settings between Instances; installing it replaces only the included Categories.

When it starts, before it reads any configuration, the Instance installs the deployment files (`.thd`) in its `deployments` folder, so a new Instance can be set up with known configurations and packages. The installer's `/DEPLOYMENT` option (or its **Deployment** page) copies a file there, and `--deploymentPath` or `TRAKHOUND_DEPLOYMENT_PATH` reads them from another folder or a single file (ex. a read-only Docker mount). Each file is installed once, with a backup that a failed install is rolled back from, and **Deploy > Install** lists the results. See the [User Guide](docs/instance-user-guide.md) (section 2.6).

## Monitoring and Diagnostics

The admin UI's Explorer shows what a running Instance is doing:

- **CPU by Module** (**Instance > Monitor**) – the CPU used by each Worker, Function, Api and App and by the admin UI, with a 10 minute chart. **Unattributed** is the process CPU no Module accounts for (the HTTP server, Services' background work, the Garbage Collector). Api and App Metrics also show CPU per Api, Endpoint and App. Measured when **Settings > Metrics > Measure CPU Usage** is on; `GET /_instances/cpu` returns the same breakdown.
- **Metrics** – request rates, timings and errors for Apis, Apps, HTTP, Functions, Services, Workers, Streams and MCP.
- **Connections** – recent HTTP and Api Requests, the browsers connected to Apps, and **Api Streams**: every Stream open to an Api, including those opened inside the Instance by Apps, Workers and Functions, with the messages and data each delivered, idle Streams highlighted, recently closed Streams with their close reason, and a **Close Stream** button.
- **Logs**, **Health** checks and loaded **Modules** (with their load errors).

See the [User Guide](docs/instance-user-guide.md) (sections 2.3.1 and 2.3.7). To measure an Instance under load, see the k6 scenarios in [tests/load](tests/load/README.md).

## Creating a Package

Install the project templates:

```bash
dotnet new install TrakHound.DotNet.Templates
```

Then create a new package from one of the templates:

| Template | Command |
| --- | --- |
| Api | `dotnet new trakhound.api` |
| App | `dotnet new trakhound.app` |
| Function | `dotnet new trakhound.function` |
| Worker | `dotnet new trakhound.worker` |
| Service | `dotnet new trakhound.service` |
| Identity | `dotnet new trakhound.identity` |
| Host (runs an Api, Worker or Function standalone) | `dotnet new trakhound.host` |

Each template also creates an `AGENTS.md` (with a `CLAUDE.md` that imports it) telling AI coding tools how to build, run and write that kind of package. See [templates/TrakHound.DotNet.Templates](templates/TrakHound.DotNet.Templates/README.md) for details.

### Example Api

```csharp
public class Controller : TrakHoundApiController
{
    [ApiGet]
    public async Task<TrakHoundApiResponse> Get([FromQuery] string name)
    {
        return Ok($"Hello {name}");
    }

    [ApiGet("status")]
    [ApiMcpTool(Description = "Returns the current status")]
    public async Task<TrakHoundApiResponse> GetStatus()
    {
        return Ok("OK");
    }

    [ApiPut("{key}")]
    public async Task<TrakHoundApiResponse> Put([FromRoute] string key, [FromQuery] string value)
    {
        await Variables.Set(key, value);
        return Ok();
    }
}
```

Every package includes a `trakhound.package.json` manifest:

```json
{
  "id": "TrakHound.Example.Api",
  "category": "api",
  "metadata": {
    "publisher": "",
    "description": "",
    ".defaultRoute": "example/api"
  }
}
```

Each Api also gets an OpenAPI document at `_api/openapi/{apiId}`, linked from its page in the admin UI.

The complete example is in [examples/TrakHound.Example.Api](examples/TrakHound.Example.Api). It works with an example App, Function and Worker: see [examples](examples/README.md). The `debug/` projects let you run a package as a standalone ASP.NET Core app that connects to a running Instance.

### Test and Host a Package

- **Unit tests** – the `TrakHound.Testing` NuGet package runs Functions, Workers, Api Controllers, Services and Identity providers in a unit test (xUnit, NUnit or MSTest) without an Instance. `TrakHoundTestContext` creates them the way the Instance does, with a temporary Volume, Variables, Parameters and an in-memory Client. See the [Developer Guide](docs/developer-guide.md) (section 2.9).
- **Standalone** – an Api, Worker or Function can also run without an Instance: as a console app, a Windows Service, a systemd service, an IIS application, a container or a cloud web app. Create a host project with `dotnet new trakhound.host` (or `th host create` in the package's folder). It references the package project, so the package doesn't change, and its Client can still connect to an Instance. See the [Developer Guide](docs/developer-guide.md) (section 2.10).

### Build and Publish a Package

Build the project into a `.thp` package file:

```bash
th packages dotnet --output ./out
```

Install the `.thp` from the admin UI (**Packages > Upload**), then add a configuration for it under **Config**. To publish to the TrakHound package server in a Publish Target in `trakhound.package.publish.json`:

```bash
th packages dotnet --publish
```

To publish several packages to the same servers, put one `trakhound.package.publish.json` in a parent folder (see [trakhound.package.publish.sample.json](trakhound.package.publish.sample.json)); the CLI uses the nearest one. `--target` picks one of its Publish Targets (ex. `th packages dotnet --publish --target release`). The version is the `version` prefix in `trakhound.package.json` plus the git height, so the same commit always publishes the same version (see the [CLI README](cli/README.md#versions)). A Publish Target's `versionSuffix` publishes prereleases instead (ex. `0.3.4-dev.20261114153000` for development builds), and `--skip-existing` skips a server that already has the version when it was published from the same commit.

An Instance uses the newest stable version of a package unless a configuration includes prereleases (**Include Prereleases**, a `*-*` version), and the Admin interface offers prerelease updates only to those configurations.

In Visual Studio, the TrakHound extension (installed with the CLI) adds a **Publish TrakHound Package** command that runs the same publish, and a **Publish TrakHound Packages...** command that publishes the projects in a `trakhound.build.json` together. See the [Developer Guide](docs/developer-guide.md) for the full walkthrough.

## CLI

The TrakHound CLI (`th`) manages packages, applications, and deployments:

```bash
th packages list
```

```bash
th packages install TrakHound.Packages.Api
```

See [cli/README.md](cli/README.md) for installation instructions and the full list of commands.

## Contributing

Bug reports, fixes and new features are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and send a pull request, and follow the [Code of Conduct](CODE_OF_CONDUCT.md). Report security problems privately, as described in [SECURITY.md](SECURITY.md), not in a public issue.

## License

This project is licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for the third-party code it includes.

`libraries/TrakHound.Blazor.Diagrams` is a copy of [Z.Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams), Copyright (c) 2018 zHaytam, under the MIT License (see [its LICENSE](libraries/TrakHound.Blazor.Diagrams/LICENSE)).
