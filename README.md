<img alt="TrakHound" src="img/trakhound-icon-background.svg" width="96">

# TrakHound.Framework

TrakHound Framework is a modular .NET 10 platform for building, deploying, and hosting **APIs, Blazor Apps, Functions, Workers, Services, and Identity providers** as installable packages. A TrakHound **Instance** hosts these packages, routes requests to them, and provides a web-based Admin interface for configuration, exploration, and deployment. It's built for manufacturing and industrial data: collecting it from machines, storing it, and serving the Apis and Apps built on it.

## Documentation

| Guide | For |
| --- | --- |
| [Installation Guide](docs/instance-installation-guide.md) ([PDF](docs/instance-installation-guide.pdf)) | Installing, upgrading and uninstalling an Instance as a Windows Service or in IIS, in a container, or as a systemd service on Linux |
| [User Guide](docs/instance-user-guide.md) ([PDF](docs/instance-user-guide.pdf)) | Using the Admin interface and configuring a running Instance |
| [Developer Guide](docs/developer-guide.md) ([PDF](docs/developer-guide.pdf)) | Building, debugging and publishing packages |

## Quick Start

### Windows

1. Install the [ASP.NET Core Runtime 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (the installer stops with a link if it's missing).
2. Download `trakhound-instance-<version>-install.exe` from the [latest release](https://github.com/TrakHound/TrakHound.Framework/releases/latest) and run it as an administrator. Choose **Windows Service**, enter the first admin account on the **Admin Account** page, and keep the default packages (a Welcome page and the examples).
3. When Setup finishes, it opens the Admin interface at `http://localhost:8472/_admin`. Sign in with the account you entered.

See the [Installation Guide](docs/instance-installation-guide.md#3-install-as-a-windows-service) (section 3) for each page of Setup.

### Docker

```bash
docker run -d --name trakhound -p 127.0.0.1:8472:8472 trakhound/instance
```

On its first start, the container creates an admin account named `admin` with a generated password, shown once in its output (`docker logs trakhound`). Open `http://localhost:8472/_admin`, sign in, and install a package from **Packages > Browse**. See the [Installation Guide](docs/instance-installation-guide.md#62-containers-and-scripted-installs) (section 6.2) for setting the account yourself.

For Linux, IIS and Docker Compose, see [Install an Instance](#install-an-instance).

## Concepts

| Term | Description | Guide |
| --- | --- | --- |
| **Instance** | The ASP.NET Core / Blazor host process that loads packages, exposes HTTP endpoints, and serves the Admin interface. | [Installation](docs/instance-installation-guide.md) |
| **Package** | A deployable unit described by a `trakhound.package.json` manifest. Each package has a `category`: `api`, `app`, `function`, `worker`, `service`, or `identity`. | [Developer 8](docs/developer-guide.md#8-package-file-reference) |
| **Api** | A controller-style HTTP API (`TrakHoundApiController`) mounted at a configurable route. Supports GET/PUT/POST/DELETE and streaming responses (WebSocket or Server-Sent Events). Endpoints can also be served as MCP Tools. | [Developer 4.1](docs/developer-guide.md#41-api) |
| **App** | A Blazor UI application hosted within the Instance. | [Developer 4.2](docs/developer-guide.md#42-app) |
| **Function** | A unit of code that is run on demand or on a schedule. A **Job** is a named set of Parameters for a Function, and a **Scheduled Task** runs a Function (optionally with a Job) on a CRON schedule. | [User 2.3.4](docs/instance-user-guide.md#234-functions) |
| **Worker** | A long-running background process. | [Developer 4.4](docs/developer-guide.md#44-worker) |
| **Service** | A backing provider for data primitives: Blobs, Key-Values, Messages, Message Queues, Tables, and Time Series. | [Developer 4.5](docs/developer-guide.md#45-service) |
| **Router** | Sends Service calls to Services (or other Routers). Each Route tries its Targets in order and falls back to the next one only on the results that Target lists (by default `NotFound`, `NotAvailable`, `Timeout` and `InternalError`). | [User 2.5](docs/instance-user-guide.md#25-config) |
| **Identity** | An authentication provider (token, OpenID Connect, LDAP, or the built-in API keys and users) used to secure Apis, Apps, and Service operations. | [User 2.7](docs/instance-user-guide.md#27-security) |
| **Volume** | A scoped storage area provided to packages for files and configuration. | [Developer 6](docs/developer-guide.md#6-parameters-secrets-volumes-and-logging) |
| **Group** | Organizes Apis, Apps, Functions and Workers (ex. the Modules of one line) and sets the Router of those that don't name one. Groups never restrict anything; a Zone can list a Group (`group:<id>`) to restrict every Module in it. | [User 2.5](docs/instance-user-guide.md#25-config) |
| **Zone** | A group of Modules (Apis, Apps, Functions, Workers, Services) whose Client calls are limited to what the Zone's Rules allow. | [User 2.7.1](docs/instance-user-guide.md#271-zones) |
| **Configuration Variable** | A value shared by every configuration in a profile, referenced from parameter values as `${name}` (or `${env:NAME}` for an environment variable). | [User 2.5](docs/instance-user-guide.md#25-config) |

## Getting Started

### Install an Instance

- **Windows:** the installer (`trakhound-instance-<version>-install.exe`) installs the Instance as a Windows Service (served on port **8472** by default) or into an IIS Application.
- **Docker:** the `trakhound/instance` image runs an Instance, and `trakhound/instance-dev` adds a Package Server for developing packages. [examples/docker](examples/docker/README.md) has Docker Compose files for each, for running an Instance with HTTPS (Caddy), MQTT, PostgreSQL / TimescaleDB, SQL Server or Redis, and for all of them together in a simulated plant.
- **Linux:** the release archive (`trakhound-instance-<version>-linux-x64.tar.gz` or `-linux-arm64`) includes the .NET runtime and an `install.sh` that installs, upgrades or uninstalls the Instance as a systemd service.

See the [Installation Guide](docs/instance-installation-guide.md) for requirements, upgrades, silent installs and uninstalling.

Behind a reverse proxy (ex. nginx serving HTTPS), add the proxy's address to **Trusted Proxies** (**Settings > Interfaces**), so the Instance sees the real client's address and scheme. See the [Installation Guide](docs/instance-installation-guide.md#53-serve-https-with-a-reverse-proxy) (section 5.3).

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

The Instance accepts an optional configuration profile ID and the options `--configurationPath`, `--packagePath`, `--volumePath`, and `--deploymentPath` (see [Deployments](#deployments)). Once it's running, open the Admin interface in a browser to configure Apis, Apps, Functions, Workers, Services, Routers, and Identity providers.

The Admin interface can't create its first admin account. For a new configuration profile, create it with the `set-admin` command (it asks for the password), then sign in. That account is an Owner and can add more accounts with other roles in **Security > Admin Accounts**:

```bash
dotnet run --project instances/TrakHound.Instance -- set-admin <username> --profile <profile>
```

See the [User Guide](docs/instance-user-guide.md#32-admin-accounts) (section 3.2) for the other ways to create or reset the account.

## Features

### Functions and Scheduling

Functions are managed in the Admin interface's Explorer:

- **Run**: run a Function with Parameters entered directly or loaded from a Job.
- **Jobs**: named sets of Parameters for a Function, used by its next Run.
- **Schedule** and **Schedule Board**: Scheduled Tasks that run a Function (and Job) on a CRON schedule. The Board schedules by drag and drop and shows the Scheduled Tasks Worker's status.
- **Summary**: run metrics per Function and per Scheduled Task.
- **Export / Import**: copy Jobs and Scheduled Tasks between Instances.

See the [User Guide](docs/instance-user-guide.md#234-functions) (section 2.3.4).

### Configuration Variables

A parameter value in any configuration can reference a variable shared by every configuration in the profile as `${name}` (ex. `directory: ${dataPath}/files`), or an environment variable of the Instance process as `${env:NAME}`. `$${name}` is the literal text `${name}`. Variables are edited under **Config > Variables**, included in Deployments, and encrypted when they have secret names.

In a package, `GetParameter()` returns values with references replaced, and `GetConfigurationVariable("dataPath")` reads a variable by name. See the [User Guide](docs/instance-user-guide.md#25-config) (section 2.5).

### Security

Resources (Apis, Apps, and Service operations) are protected by assigning permissions to roles in **Security**. Identity providers authenticate requests and interactive sign-ins: OIDC and LDAP, the built-in `users` provider (usernames and passwords stored on the Instance, for Apps with a few users), and the built-in `api-keys` provider (keys sent as a `Bearer` token or an `X-API-Key` header). Apis and Apps that no Profile covers are public. System Resources and Service operations that no Profile covers can only be used from the same machine. See the [User Guide](docs/instance-user-guide.md#27-security) (section 2.7).

### Rate Limits

Rate Limits stop one caller (a script polling too fast, or an AI client in a loop) from slowing down the Instance for everyone else. They limit the Apis, the System API and the MCP Server's Tools by requests per window and by concurrent requests, per Identity or IP Address. They're off by default; turn them on in **Settings > Rate Limits**. They protect against mistakes, not attacks: use a reverse proxy or gateway to block heavy traffic from the internet. See the [User Guide](docs/instance-user-guide.md#35-rate-limits) (section 3.5).

### Zones

A Zone (**Config > Zones**) lists Member Modules and the Rules for what they can reach through their Client. Anything a Rule doesn't allow is denied (**Enforce**) or only recorded (**Audit**), and denied calls can be granted in one click. **Config > Zone Map** draws the Zones as a diagram, with denied calls as they happen.

Zones guard against accidental access (ex. Modules in development or written with AI coding tools). They are not a boundary against deliberately hostile code. See the [User Guide](docs/instance-user-guide.md#271-zones) (section 2.7.1).

### MCP Server

The Instance can serve an MCP (Model Context Protocol) Server at `/_mcp` so AI clients can run Functions, read Logs, query Tables, read and write Key Values, Time Series and Messages, read Volume files, and call Api Endpoints marked as Tools (`[ApiMcpTool]`). It's off by default: turn it on in **Settings > Interfaces > MCP Server** (the System API must be on too), then connect a client, for example:

```bash
claude mcp add --transport http trakhound http://localhost:8472/_mcp --header "X-API-Key: thk_..."
```

Access is restricted by Identity Profiles like the rest of the System API, and clients only see the Tools the caller can use. See the [User Guide](docs/instance-user-guide.md#210-mcp-server) (section 2.10).

### Api Catalog and API Explorer

Developers without access to the Admin interface can discover Apis on the HTTP Port:

- `/_developer/catalog` lists the Apis with only the Endpoints the caller's Identity is allowed to call.
- `/_developer/openapi/{apiId}` returns an Api's OpenAPI document, filtered the same way. (`/_api/openapi/{apiId}`, linked from the Admin interface, is the full document, on the System API.)
- `/llms.txt` ([llmstxt.org](https://llmstxt.org)) is a Markdown index of the Instance for AI agents, generated per caller.
- `TrakHound.ApiExplorer.App` browses the Catalog, sends "Try It" requests with the developer's own credentials, and shows code samples.

The System API is described by OpenAPI documents too (`/_instances/openapi`). See the [User Guide](docs/instance-user-guide.md#211-api-catalog-and-api-explorer) (section 2.11).

### Deployments

A Deployment (`.thd`) bundles Packages and Configurations to install on another Instance, or only some Configuration Categories to copy settings between Instances. When it starts, the Instance installs the deployment files in its `deployments` folder (or `--deploymentPath`), so a new Instance can be set up with known configurations and packages. See the [User Guide](docs/instance-user-guide.md#26-deploy) (section 2.6).

### Monitoring and Diagnostics

The Admin interface's Explorer shows what a running Instance is doing:

- **CPU by Module**: the CPU used by each Worker, Function, Api and App.
- **Metrics**: request rates, timings and errors for Apis, Apps, HTTP, Functions, Services, Workers, Streams and MCP.
- **Connections**: recent requests, the browsers connected to Apps, and every open Api Stream.
- **Logs**, **Health** checks and loaded **Modules** (with their load errors).

See the [User Guide](docs/instance-user-guide.md#231-instance) (sections 2.3.1 and 2.3.7). To measure an Instance under load, see the k6 scenarios in [tests/load](tests/load/README.md).

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
    [ApiGet(Description = "Returns a greeting.")]
    public async Task<TrakHoundApiResponse> Get(
        [FromQuery(Description = "Who to greet")] string name
        )
    {
        return Ok($"Hello {name}");
    }

    [ApiGet("status", Description = "Returns the current status.")]
    [ApiMcpTool]
    public async Task<TrakHoundApiResponse> GetStatus()
    {
        return Ok("OK");
    }

    [ApiPut("{key}", Description = "Sets a Variable of the Instance.")]
    public async Task<TrakHoundApiResponse> Put(
        [FromRoute(Description = "The Variable key")] string key,
        [FromQuery(Description = "The value to set")] string value
        )
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

The complete example is in [examples/TrakHound.Example.Api](examples/TrakHound.Example.Api). It works with an example App, Function and Worker: see [examples](examples/README.md).

### Test and Host a Package

- **Unit tests**: the `TrakHound.Testing` NuGet package runs Functions, Workers, Api Controllers, Services and Identity providers in a unit test (xUnit, NUnit or MSTest) without an Instance. `TrakHoundTestContext` creates them the way the Instance does, with a temporary Volume, Variables, Parameters and an in-memory Client. See the [Developer Guide](docs/developer-guide.md#29-unit-test-it) (section 2.9).
- **Standalone**: an Api, Worker or Function can also run without an Instance: as a console app, a Windows Service, a systemd service, an IIS application, a container or a cloud web app. Create a host project with `dotnet new trakhound.host` (or `th host create` in the package's folder). It references the package project, so the package doesn't change, and its Client can still connect to an Instance. See the [Developer Guide](docs/developer-guide.md#210-run-it-standalone) (section 2.10).

### Build and Publish a Package

Build the project into a `.thp` package file:

```bash
th packages dotnet --output ./out
```

Install the `.thp` from the Admin interface (**Packages > Upload**), or push it to an Instance that runs the Dev Package Server (`TrakHound.Packages.Dev.Api`, included in the `trakhound/instance-dev` image) and install it from **Packages > Browse**:

```bash
th packages push ./out/<package>.thp -s http://localhost:8472 --organization Dev
```

Then add a configuration for it under **Config**. To publish to the package servers in `trakhound.package.publish.json`:

```bash
th packages dotnet --publish
```

The version is the `version` prefix in `trakhound.package.json` plus the git height, so the same commit always publishes the same version, and a Publish Target's `versionSuffix` publishes prereleases. See the [CLI README](cli/README.md#versions) for versions and Publish Targets, and the [Developer Guide](docs/developer-guide.md#9-building-and-publishing) (section 9) for the full walkthrough, including the Visual Studio extension.

## CLI

The TrakHound CLI (`th`) manages packages, applications, and deployments. Install it from the [latest release](https://github.com/TrakHound/TrakHound.Framework/releases/latest), then:

```bash
th packages list
```

```bash
th packages install TrakHound.Packages.Api
```

See [cli/README.md](cli/README.md) for installation on Windows and Linux and the full list of commands.

## Repository

### Layout

```
cli/         TrakHound.CLI (`th`) – create, pack, publish and install packages and deployments
debug/       Debug hosts for running Api, App, Function, Worker, Service and Identity packages outside of an Instance, and TrakHound.Testing for unit tests
examples/    Example packages (an Api, App, Function and Worker that work together, and an MQTT to Time Series Worker) and Docker Compose examples
extensions/  TrakHound.VisualStudio – Visual Studio extension to publish packages through the CLI
img/         The icon used by this README
instances/   TrakHound.Instance – the host application and Admin interface
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

The `debug/` projects are NuGet packages too: `TrakHound.Debug`, `TrakHound.Debug.AspNetCore` and `TrakHound.Debug.Apps` run a package locally without an Instance, and `TrakHound.Testing` runs its modules in unit tests. See the [Developer Guide](docs/developer-guide.md#10-libraries) (section 10).

Official builds use Font Awesome Pro icons, which are licensed and aren't in this repository, so a build from source shows a generic outline wherever an icon would be. To use your own license, put its files in `libraries/TrakHound.Blazor.Hosting/wwwroot/css/font-awesome` (ignored by git). They're used when you run or publish from source but never packed, and a pack that would include them fails.

### Modules

| Package | Category | Description |
| --- | --- | --- |
| `TrakHound.Packages.Api` | api | Download, upload, and manage packages |
| `TrakHound.Packages.Dev.Api` | api | A Package Server for development that keeps Packages as files in its Volume, with no other Services to set up |
| `TrakHound.Packages.App` | app | UI for managing packages |
| `TrakHound.ApiExplorer.App` | app | Browse the Apis you can use, try them out, and download their OpenAPI documents |
| `TrakHound.Welcome.App` | app | A home page (at `/`) for development Instances: what's installed, where it's served, and how to get started. Don't install it in production ([README](modules/TrakHound.Welcome.App/README.md)) |
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

The first four packages work together around a simulated sensor: install `TrakHound.Example.App` to install and configure all four. See [examples](examples/README.md).

| Package | Category | Description |
| --- | --- | --- |
| `TrakHound.Example.Worker` | worker | Reads a simulated sensor and writes the recent readings to a shared Volume |
| `TrakHound.Example.Function` | function | Summarizes the recent readings (count, minimum, maximum, average) |
| `TrakHound.Example.Api` | api | Serves the readings (with a stream), runs the Function, and reads and writes Variables (route `example/api`) |
| `TrakHound.Example.App` | app | A page that shows the readings from the Api and the Function's summary (route `example`) |
| `TrakHound.Example.MqttToTimeSeries` | worker | Subscribes to Messages (ex. from an MQTT Broker) and writes each value to a Time Series Service |

[examples/docker](examples/docker/README.md) has Docker Compose files that run an Instance with the servers the Service packages use. Its `plant` example runs `TrakHound.Example.MqttToTimeSeries` with simulated machines, Mosquitto and TimescaleDB.

## Contributing

Bug reports, fixes and new features are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and send a pull request, and follow the [Code of Conduct](CODE_OF_CONDUCT.md). Report security problems privately, as described in [SECURITY.md](SECURITY.md), not in a public issue.

## License

This project is licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for the third-party code it includes.

`libraries/TrakHound.Blazor.Diagrams` is a copy of [Z.Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams), Copyright (c) 2018 zHaytam, under the MIT License (see [its LICENSE](libraries/TrakHound.Blazor.Diagrams/LICENSE)).
