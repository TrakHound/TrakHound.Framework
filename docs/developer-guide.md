![TrakHound](images/trakhound-guide-header.png)

# TrakHound Developer Guide

Oct 20, 2026 · Patrick Ritchie

## Contents

1. Overview
    - 1.1 Package types
    - 1.2 What you need
2. Getting started
    - 2.1 Install the templates
    - 2.2 Create a package
    - 2.3 Run it locally
    - 2.4 Build the package file
    - 2.5 Install it on an Instance
    - 2.6 Add a configuration
    - 2.7 Publish to a package server
    - 2.8 Debug it inside an Instance
    - 2.9 Unit test it
    - 2.10 Run it standalone
3. How packages run
    - 3.1 Loading
    - 3.2 Configurations
    - 3.3 What your code receives
4. Package types
    - 4.1 Api
    - 4.2 App
    - 4.3 Function
    - 4.4 Worker
    - 4.5 Service
    - 4.6 Identity
5. Calling the Instance from your package
6. Parameters, secrets, volumes and logging
7. Security
    - 7.1 Licensing a package
8. Package file reference
9. Building and publishing
10. Libraries
11. Troubleshooting

## 1. Overview

A TrakHound package is a .NET project that an Instance loads and runs: an API, a web App, a Function, a Worker, a Service or an Identity provider. You build it with the TrakHound templates, test it on your machine, pack it into a `.thp` file, and install it on an Instance. A **configuration** on the Instance then runs the package with its own route, parameters and storage.

This guide is for developers writing packages. For installing an Instance see the TrakHound Instance Installation Guide (`instance-installation-guide.pdf`); for operating one see the TrakHound Instance User Guide (`instance-user-guide.pdf`).

### 1.1 Package types

The type is set by `category` in the package's `trakhound.package.json`.

| Category | What it is | You implement | Template |
| --- | --- | --- | --- |
| `api` | HTTP endpoints served at a route | `TrakHoundApiController` | `trakhound.api` |
| `app` | Blazor web pages served at a route | Razor components (`TrakHoundComponentBase`) | `trakhound.app` |
| `function` | Code that runs on demand or on a schedule and returns a result | `TrakHoundFunction` | `trakhound.function` |
| `worker` | A background process that starts and stops with the Instance | `TrakHoundWorker` | `trakhound.worker` |
| `service` | A storage backend for Blobs, Key Values, Messages, Message Queues, Tables or Time Series | `TrakHoundService` + a service interface | `trakhound.service` |
| `identity` | A sign-in provider (tokens, redirects or credentials) | `TrakHoundIdentityProviderBase` | `trakhound.identity` |

The example packages (`TrakHound.Example.Api`, `.App`, `.Function` and `.Worker`, in `examples/` in the repository) show an Api, App, Function and Worker that work together: the Worker writes simulated sensor readings to a shared Volume, the Api serves them and runs the Function to summarize them, and the App shows them. Install `TrakHound.Example.App` from Packages to install and configure all four.

### 1.2 What you need

| Tool | Notes |
| --- | --- |
| .NET 10 SDK | Packages target `net10.0` |
| TrakHound CLI (`th`) | Builds, publishes and installs packages. Installed with the TrakHound CLI installer |
| A TrakHound Instance | To test the package end to end. A local Instance on the default port 8472 works with the templates as they are (set `--instanceUrl` for another one) |
| Visual Studio 2022 17.14 or later (optional) | The TrakHound extension adds the **Publish TrakHound Package** and **Publish TrakHound Packages...** commands. It is an option in the CLI installer |

## 2. Getting started

This walkthrough builds an API package, runs it on your machine, and deploys it to an Instance. The steps were tested against TrakHound 0.9.6.

### 2.1 Install the templates

```
dotnet new install TrakHound.DotNet.Templates
```

This adds six templates: `trakhound.api`, `trakhound.app`, `trakhound.function`, `trakhound.worker`, `trakhound.service` and `trakhound.identity`.

### 2.2 Create a package

```
dotnet new trakhound.api -n Contoso.Hello --publisher "Contoso"
```

The project name becomes the package ID, so use a unique dotted name. The template options are:

| Option | Default | What it sets |
| --- | --- | --- |
| `--trakhoundVersion` | The version the templates were released with | The TrakHound NuGet package version referenced by the project |
| `--instanceUrl` | `http://localhost:8472` | `instanceUrl` in `trakhound.debug.json`: the Instance the debug host connects to |
| `--publisher` | (empty) | `publisher` in `trakhound.package.json` |
| `--description` | (empty) | `description` in `trakhound.package.json` |
| `--managementServer` | (empty) | The package server of the `default` Publish Target in `trakhound.package.publish.json`. The file is only created if this or `--organization` is set |
| `--organization` | (empty) | The organization of the `default` Publish Target in `trakhound.package.publish.json` |
| `--skipRestore` | `false` | Skip the NuGet restore after the project is created |

By default the project references the TrakHound version the templates were released with. If your Instance runs an older TrakHound version, set `--trakhoundVersion` to the Instance's version: a package can't use a newer TrakHound than its Instance (section 3.1).

The API project contains:

| File | Purpose |
| --- | --- |
| `Controller.cs` | Your endpoints |
| `Program.cs` | A local debug host. Not included in the package |
| `trakhound.debug.json` | The debug host's settings: the Instance, credentials and configuration parameters. Not included in the package |
| `Properties/launchSettings.json` | How `dotnet run`, Visual Studio and VS Code start the debug host (port 5091, Development environment, opens the Api Debugger) |
| `trakhound.package.json` | The package manifest (section 8) |
| `trakhound.package.publish.json` | Where to publish : the Publish Targets (section 8). Only created if `--managementServer` or `--organization` is set |
| `publish-package.bat` / `.sh` | Builds and publishes the package with the CLI |
| `.gitignore` | Leaves `bin`, `obj`, the debug `volume` folder and `.thp` files out of source control |
| `AGENTS.md` / `CLAUDE.md` | Instructions for AI coding tools (ex. Claude Code): the commands, conventions and rules for the package type. `CLAUDE.md` imports `AGENTS.md`. Edit them as your package grows |

### 2.3 Run it locally

Each template's `Program.cs` hosts the package without an Instance:

```
cd Contoso.Hello
dotnet run
```

Then open `http://localhost:5091/?name=world`, or the Api Debugger at `http://localhost:5091/_debug`. The Api Debugger lists the endpoints and streams in the OpenAPI document the Instance creates for your Api (the same one it serves at `_api/openapi/{apiId}` and lists in the Api Catalog, served by the debug host at `/_debug/openapi.json`), so a missing description or a wrong parameter type shows up there before you install the package. Its version (shown in the header) is the one in `trakhound.package.json`, or, when that has none, the one in `trakhound.package.publish.json`. It sends requests (with a form for the parameters and an editor for the body, and shows the response: JSON, text, images or a download), connects to stream endpoints over WebSocket or Server-Sent Events (shows each message as it arrives, and sends input over a WebSocket), and shows the debug host's log (each request, and what the controller writes with `Log(...)`) as it's written. Enter in a parameter field sends the request or connects the stream (Ctrl+Enter in the body editor; Enter in a stream's input box sends it), **Copy** copies a response or a stream message, the endpoint list and the log can be hidden, and the log can be resized by dragging its top edge. A missing required parameter is marked on its field, and the URL the request goes to is shown under the route as you fill in the parameters. The launch profile in `Properties/launchSettings.json` sets the port and the Development environment; Visual Studio and VS Code use the same profiles. The Function and Identity templates have a profile for each way to run them (ex. a Function with other input), and the App template runs at `http://localhost:5092`.

The debug host (`TrakHoundDebugHost`, from the `TrakHound.Debug` NuGet package) reads `trakhound.debug.json` and creates the package's modules the same way the Instance does, with a client for the Instance, a local `volume` folder and Variables. It prints whether it reached the Instance. It runs even when no Instance is running, but calls your code makes through `Client` need one.

```json
{
  "instanceUrl": "http://localhost:8472",
  "apiKey": "",
  "parameters": {
    "interval": 5000
  }
}
```

| Setting | Default | What it sets |
| --- | --- | --- |
| `instanceUrl` | `http://localhost:8472` | The Instance `Client` connects to |
| `apiKey` / `bearerToken` | (none) | Credentials for an Instance that requires them |
| `parameters` | (none) | The configuration parameters, as set in the package's configuration on an Instance (`Configuration.GetParameter(...)`) |
| `input` | (none) | Functions only: the input parameters the Function runs with when none are passed to `dotnet run` |
| `configurationId` | `debug` | The ID of the configuration |
| `volumePath` | `volume` | The folder used as the Volume, relative to the build output |
| `theme` | `light` | Apps only: the Theme shown until one is picked (`light` or `dark`) |

The build copies the file to the build output, and `th packages dotnet` leaves it out of the package. Keep real credentials out of source control: leave `apiKey` empty in the committed file, or don't commit the file.

The debug host binds parameters and returns errors the same way the Instance does: a body that can't be read returns 400, and an exception in an endpoint returns 500 with its message (the stack trace is printed to the console). Entries the controller writes with `Log(...)` are printed to the console and shown in the Api Debugger. Stream endpoints (`[ApiStream]`) are served the same way the Instance serves them: a WebSocket, or a GET with `Accept: text/event-stream` (Server-Sent Events), to the stream's route, with each message a JSON response (`statusCode`, `contentType`, Base64 `content`, `parameters`).

### 2.4 Build the package file

```
th packages dotnet --output ./out
```

The CLI builds the project as a library, leaves out the debug host and the TrakHound assemblies the Instance already has, and writes `out/Contoso.Hello-0.0.1.thp`. Section 9 covers the options.

### 2.5 Install it on an Instance

Choose one:

- **Admin interface:** Packages > **Upload**, drop the `.thp` file, then **Install**.
- **CLI, into an Instance folder:** `th packages install --file ./out/Contoso.Hello-0.0.1.thp -p "C:\Program Files\TrakHound\Instance"`. A running Instance watches its `packages` folder and picks up the package within a few seconds.
- **CLI, to an Instance you can't reach as a folder** (ex. one in a Docker container, or on another machine): push it to the `TrakHound.Packages.Dev.Api` package on that Instance, then install it from Packages > **Browse**.

**Using the Dev Api:** it's a package server for development that the Instance serves itself, keeping each package as a file in its volume, so it needs no other services. It only stores packages; installing one is still done in the Admin interface. In the Docker image, starting the container with `TRAKHOUND_DEV_UPLOAD_TOKEN` set to a long random value does the setup below for you, with that value as the `uploadToken` (see the Instance Installation Guide, section 6.2). Otherwise, set it up once:

1. Install `TrakHound.Packages.Dev.Api` on the Instance and add a configuration for it with the route `packages`.
2. Set its `uploadToken` parameter to a long random value. Uploads are refused without it (or an Instance API key).
3. Add a package source (Packages > **Sources**) with the Base URL `http://localhost:8472` (the Instance's own address, also inside a container) and an Organization, ex. `Dev`.

Then, for each build:

```powershell
th packages dotnet --output ./out --version-suffix dev.1
$env:TRAKHOUND_API_TOKEN = "<uploadToken>"
th packages push ./out/Contoso.Hello-0.0.1-dev.1.thp -s http://localhost:8472 --organization Dev
```

Use the address you reach the Instance at, ex. the port a container publishes. Give each build a new `--version-suffix` (`dev.2`, `dev.3`): the same version is refused, and the Instance only offers a newer version as an update (Packages > **Updates**). Unsigned packages install under the default Package Verification mode (Warn), but not under Require or RequireApproval. Uninstall the Dev Api from Instances that don't need it. Its [README](../modules/TrakHound.Packages.Dev.Api/README.md) has the parameters and how to move its packages to a package server.

For the fastest loop with an Instance you can reach as a folder, use `th dev run` instead (section 2.8).

### 2.6 Add a configuration

Installing a package does not run it. In the Admin interface:

1. Open **Config** > **Api** and click **Add Api**.
2. Choose **Installed**, select `Contoso.Hello`, and click **Continue**.
3. Enter a **Name** and a **Route**, ex. `contoso/hello`.
4. Click **Add Api**.

The API now answers at `http://<instance>/contoso/hello?name=world`. If it returns 404, open Explorer > Diagnostics > Modules for the load error (section 11).

### 2.7 Publish to a package server

To share the package through a TrakHound package server, set the server and organization in a Publish Target in `trakhound.package.publish.json` (in the project, or once for several projects in a parent folder, section 8), sign the CLI in to that server (section 9), and run:

```
th packages dotnet --publish
```

Or run `publish-package.bat`, or use **Publish TrakHound Package** in Visual Studio. The version is the `version` prefix in `trakhound.package.json` plus the git height (section 8), so commit your changes before publishing a new version. Instances that use that server as a package source can then install the package from Packages > **Browse**.

### 2.8 Debug it inside an Instance

The debug host (section 2.3) runs your code on its own. To run it inside an Instance, with its routing, security, zones, MCP Server and the other packages, use `th dev run`:

```
th dev run --instance C:\TrakHound\Instance
```

It builds the project (Debug, with `.pdb` files), installs it in the Instance folder, and adds a configuration for it (`dev-<package id>`). Then it watches the project and reinstalls it each time you save. The Instance picks up each change within a few seconds, without a restart. Press Ctrl+C to stop: the configuration and the package are removed.

- **The Instance:** `--instance` is the Instance's root folder (with its `packages` and `config` folders), ex. the build output of the Instance or a folder you unzipped it to. Set the `TRAKHOUND_DEV_INSTANCE` environment variable to leave it out. The Instance must be running from that folder (with the same `--profile`, default `default`). Writing to an Instance installed as a Windows Service (`C:\ProgramData\TrakHound\Instance`) needs an administrator prompt.
- **Versions:** the Instance only loads new code for a new version, so each build gets the next 4th number of the package version (ex. `0.0.1.1`, `0.0.1.2`). The configuration is set to that exact version, and the previous one is uninstalled.
- **The configuration:** its parameters are `parameters` in the project's `trakhound.debug.json` (if it has one). An Api or App gets the route in `--route`, the package's `.defaultRoute`, or its ID. A Function's `functionId` is the package ID.
- **Debugging:** attach your debugger to the Instance process. `th dev run` prints its process ID (read from the Instance's Boot History). The debugger stays attached across reloads, and breakpoints bind to each new version.
- **Build errors:** a failed build is printed and the Instance keeps running the last version that built.
- `--no-watch` builds and installs once (and keeps it). `--keep` keeps the configuration and package when you stop. `th dev clean` removes what `th dev run` left in the Instance, ex. after `--keep` or a crash.
- Apps are reloaded too, but an App's old version can stay in memory, so restart the Instance now and then during a long session.

### 2.9 Unit test it

The `TrakHound.Testing` NuGet package runs your modules in a unit test (xUnit, NUnit or MSTest) without an Instance. Add a test project that references your package project and `TrakHound.Testing`, then create a `TrakHoundTestContext` in each test:

```csharp
public class CounterFunctionTests : IDisposable
{
    private readonly TrakHoundTestContext _context = new TrakHoundTestContext();

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task AddsToTheCount()
    {
        _context.Parameters["collection"] = "counts";

        var response = await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a", ["by"] = "5" });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("5", (await _context.Client.Services.KeyValues.Read("counts", "a")).Result);
    }
}
```

The context gives modules what the Instance does: a Volume and Variables in a temporary folder (deleted on `Dispose`), the configuration `Parameters` you set, and `Client`. It creates modules the same way the Instance does:

| Method | Creates |
| --- | --- |
| `CreateFunction<T>()` / `RunFunction<T>(input)` | A Function. `RunFunction` applies the `FunctionParameter` defaults and returns 400 for invalid input, like the Instance |
| `CreateWorker<T>()` | A Worker (call `Start` and `Stop`) |
| `CreateApiController<T>()` | An Api Controller, initialized (`OnInitialize` and `OnInitializeAsync`). Call its endpoint methods directly |
| `CreateService<T>()` | A Service |
| `CreateIdentityProvider<T>()` / `CreateIdentityContext(request)` | An Identity provider, and a context with the request headers and query parameters you set |

What the modules log is in `_context.Logs` (and `GetLogs(TrakHoundLogLevel.Warning)` for warnings and errors).

`Client` is a `TrakHoundTestClient`:

- **`Client.Services.KeyValues`, `Blobs` and `TimeSeries`** are in memory. `Client.Services.Use(service)` puts a Service of your own (ex. `_context.CreateService<MyService>()`) behind the Key Value, Blob, Table and Time Series calls it implements.
- **`Client.Functions.Run`** runs what you register: `_context.RegisterFunction<OtherFunction>("other-function")`, or `Client.Functions.Register("id", parameters => ...)` for a fake response. `Client.Functions.Runs` lists the Runs your code requested.
- **Everything else** (ex. `Client.Api`, `Client.Instances`, `Client.Services.Messages`) throws a `NotSupportedException` naming the property. Set that property to your own implementation, ex. a mock.

### 2.10 Run it standalone

A package runs on an Instance, but an Api, a Worker or a Function can also run on its own: as a console app, a Windows Service, a Linux (systemd) service, an IIS application, a container or a cloud web app. `TrakHound.Hosting` and `TrakHound.Hosting.AspNetCore` run the module with the standard .NET host, so each of those works the way it does for any .NET app.

Create a host project next to the package project with the `trakhound.host` template. It references the package project, so the package itself doesn't change:

```
dotnet new trakhound.host -n MyCompany.MyApi.Host --module MyCompany.MyApi --kind api
```

`--kind` is `api`, `worker` or `function`. `th host create` (in the package project's folder) does the same and fills it in for you: it picks the kind from the package's category, finds the module class with its namespace, copies `trakhound.debug.json` into the host's `appsettings.Development.json`, and with `--api-key` generates an API key (see the [CLI README](../cli/README.md#host-commands)). The host has a `Program.cs`, the settings in `appsettings.json`, and a `Dockerfile` (see the [template's page](../templates/TrakHound.DotNet.Templates/TrakHound.Host.Template.md) for running it as a service or in IIS). The Api host's `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
builder.Services.AddTrakHoundApiKeyAuthentication();
builder.Services.AddHealthChecks().AddTrakHoundInstance();
builder.Services.AddWindowsService(); // Runs as a Windows Service or a systemd service when started as one
builder.Services.AddSystemd();

var app = builder.Build();
var api = app.MapTrakHoundApi(typeof(Controller).Assembly);
api.Group.RequireAuthorization(); // Every endpoint and stream needs an API key (or any other ASP.NET Core authentication you add)
app.MapTrakHoundOpenApi(api);     // The OpenAPI document at /openapi.json
app.MapHealthChecks("/health");
app.Run();
```

A Worker host uses `Host.CreateApplicationBuilder(args)` with `AddTrakHoundWorker<Worker>()`: the Worker starts when the host starts and stops when it stops. A Function host runs the Function once with `host.RunTrakHoundFunction<Function>()`, writes its response (JSON) to the console and exits with 0 when the Run succeeds, for Task Scheduler, cron or a container job. `app.MapTrakHoundFunction<Function>("/run")` runs it for each POST instead (the input is the query string and a JSON object in the body). `AddTrakHoundService<Service>()` adds a Service as a singleton for your own code to call.

The settings are the `"TrakHound"` section of `appsettings.json`, environment variables (ex. `TrakHound__InstanceUrl`) or the command line (ex. `--TrakHound:Parameters:interval=1000`), with the same names as `trakhound.debug.json`:

```json
{
  "TrakHound": {
    "InstanceUrl": "https://instance.example.com",
    "ApiKey": "...",
    "ConfigurationId": "barcodes",
    "ApiKeys": [ { "Name": "line-1", "Hash": "..." } ],
    "Parameters": { "interval": "1000" }
  }
}
```

| Setting | What it does |
| --- | --- |
| `InstanceUrl` | The Instance the Client connects to. Without it, the module runs on its own and every Client call fails with an error that says so |
| `ApiKey`, `BearerToken` | Credentials for the Instance |
| `ConfigurationId` | The module's Configuration ID (`default` when not set) |
| `VolumePath`, `VariablesPath` | Where the Volume (`volume` by default) and the Variables (the application's folder by default) are kept. Relative paths are relative to the application's folder |
| `Parameters` | The Configuration Parameters. Values are strings; `GetParameter<T>` converts them |
| `ApiKeys` | The keys callers of an Api host send, in the `X-API-Key` header (or `Authorization: Bearer {key}`). Each has a `Name` (the signed in user) and the key's SHA-256 hash (hex) in `Hash`, or the key itself in `Key` when it comes from a secret store (ex. `TrakHound__ApiKeys__0__Key`). These aren't the Instance's API keys |
| `Input` | Function hosts: the input Parameters of the Run |

The host connects to the Instance when it starts. An Instance that can't be reached is logged as a warning and the module still starts; the health check reports it as Degraded. What the module logs goes to `ILogger`, so the host's log providers (the console, the Windows Event Log, Application Insights) receive it. The OpenAPI document is named and versioned from the package's `trakhound.package.json` (and `trakhound.package.publish.json`), which the template copies to the host's output.

Not supported yet: Apps, Identity providers, and signing in with an Instance's users (an Api host uses its own API keys, or any ASP.NET Core authentication you add).

## 3. How packages run

### 3.1 Loading

Installed packages live at `packages/<category>/<packageId>/<version>/dist` under the Instance folder (or `TRAKHOUND_PACKAGE_PATH`). When a configuration needs a package, the Instance:

1. Resolves the version from the configuration's `packageVersion` (`*` means the newest stable version; patterns like `1.2.*` work). A prerelease is only selected if no stable version matches, so with `1.9.0` and `2.0.0-beta` installed, `*` selects `1.9.0` and logs that a newer prerelease was skipped. To include prereleases, add `-*` (`*-*` or `1.2.*-*`), or use the exact version. In the Admin interface, that's a configuration's **Include Prereleases** switch.
2. Copies the `dist` folder to `_modules/<id>-<version>-<hash>`, so the installed files stay unlocked and a reinstall gets a fresh copy.
3. Loads `<packageId>.dll` in its own collectible assembly load context. **The assembly must be named after the package ID.**
4. Finds every non-abstract type of the expected kind in that assembly (for example every `ITrakHoundApiController`).

Your package's own dependencies load from its `dist` folder. TrakHound assemblies (`TrakHound.Common`, `TrakHound.Blazor`) are **shared with the Instance**: the package uses the Instance's copy. So a package built against a newer TrakHound version than the Instance fails to load ("Could not load file or assembly 'TrakHound.Common, Version=…'"). Build against the Instance's TrakHound version or older.

### 3.2 Configurations

A configuration binds a package to a route, parameters and storage. The Admin interface saves it as YAML at `config/<profile>/<category>/<id>.yaml`. This is the configuration from section 2.6:

```yaml
id: a2c79b77-5526-4171-b08b-b27d99f8c486
name: devguide-test
description:
route: devguide/test
volumeId:
routerId: default
packageId: Contoso.Test.Api
packageVersion: '*'
httpEnabled: true
parameters: {}
```

| Field | Applies to | Meaning |
| --- | --- | --- |
| `id`, `name`, `description` | All | Identity and display. `id` is generated |
| `packageId`, `packageVersion` | All | The package and version to run |
| `parameters` | All | Name and value pairs passed to your code (section 6) |
| `volumeId` | All | The storage folder for the package. Empty means the configuration `id` |
| `routerId` | All but Services | The router your `Client` uses to reach Services. Empty uses the router of the configuration's Group, then `default` |
| `groupId` | Api, App, Function, Worker | The Group the configuration is in (Instance User Guide, section 2.5). Groups organize configurations and set the router of those that don't name one |
| `route` | Api, App | The URL path. Must be unique |
| `httpEnabled` | Api | `false` serves the API only inside the Instance |
| `functionId` | Function | The ID callers use to run the Function |

One package can run under several configurations, each with its own route, parameters and volume.

### 3.3 What your code receives

The Instance gives each package object these members (Services get only the configuration, volume and variables):

| Member | Type | Use |
| --- | --- | --- |
| `Configuration` | `ITrakHound…Configuration` | Parameters and settings (section 6) |
| `Client` | `ITrakHoundClient` | Calls Services, Functions, other APIs and volumes (section 5) |
| `Volume` | `ITrakHoundVolume` | The package's file storage |
| `Variables` | `ITrakHoundVariableCollection` | Small persistent values: `Set`, `Read`, `ReadString` |
| `Logger` / `Log(...)` | `ITrakHoundLogger` | Writes to the Instance log history |
| `InstanceId` | `string` | The Instance's ID |

## 4. Package types

### 4.1 Api

Derive from `TrakHoundApiController` (namespace `TrakHound.Api`) and mark methods with endpoint attributes. The Instance creates each controller with its parameterless constructor, sets its members, then calls `OnInitialize` / `OnInitializeAsync`.

```csharp
public class Controller : TrakHoundApiController
{
    [ApiGet]
    public async Task<TrakHoundApiResponse> Get([FromQuery] string name)
    {
        return Ok("Hello " + name);
    }

    [ApiPost("items/{id}")]
    public async Task<TrakHoundApiResponse> Save(
        [FromRoute] string id,
        [FromBody(ContentType = "application/json")] Item item)
    {
        await Volume.WriteJson($"items/{id}.json", item);
        return Created();
    }
}
```

- **Endpoints:** `[ApiGet]`, `[ApiPost]`, `[ApiPut]`, `[ApiPatch]`, `[ApiDelete]`, `[ApiHead]` and `[ApiStream]`, each with an optional route relative to the API's route.
- **Inputs:** `[FromQuery]`, `[FromRoute]`, `[FromHeader]` and `[FromBody]`. Describe each with `Description`, ex. `[FromQuery(Description = "The first day to include")] string start`; descriptions appear in the API information, the OpenAPI document and MCP Tool inputs.
- **Headers:** `[FromHeader("X-Name")]` binds a request header (`[FromHeader]` alone uses the parameter name). Header names are case-insensitive, and a missing header leaves the parameter's default (or null). The OpenAPI document lists them as `in: header`, and MCP Tool inputs leave them out. Headers that carry the caller's credentials for the Instance are never passed to an Api, so it can't act as its caller: `Cookie`, `Authorization`, `Proxy-Authorization`, `X-API-Key`, `X-TrakHound-Module-Token` and `token` (the Token Identity Provider's header). They are withheld on an Instance, in the debug host and with `MapTrakHoundApi` (the list is `TrakHoundApiHeaders.Withheld`). An Identity Provider from another package that reads a different header isn't covered, so an Api could read that header. Authenticate your own callers with another header, ex. `X-TrakHound-Api`, which a package source sends its access token in. Apps and other packages send headers with `Client.Api.Send` and the `Get`, `Post`, `Put`, `Delete` and `Stream` overloads that take `headers`.
- **Controller route:** `[TrakHoundApiController("orders")]` on the class puts its endpoints under `<route>/orders`.
- **Responses:** `Ok`, `Created`, `Accepted`, `File`, `Redirect`, `BadRequest`, `NotAuthorized`, `NotFound` and `InternalError`. Each takes nothing, a string (text/plain), an object (JSON) or bytes with a content type.
- **Streams:** an `[ApiStream]` endpoint returns `ITrakHoundStream<TrakHoundApiResponse>`; push values with `TrakHoundStream<T>.Push`. See `examples/TrakHound.Example.Api`. Clients connect with a WebSocket, or with Server-Sent Events: a GET with `Accept: text/event-stream` (ex. a browser `EventSource` or `curl -N -H "Accept: text/event-stream"`) receives each value as a `data` event. Server-Sent Events only go from the server to the client, so input from the client needs a WebSocket. The System API subscribe routes (`/_logs/subscribe`, `/_services/messages/subscribe`, function run status and log, and so on) accept both too.
- **Message publish stream:** `/_services/messages/publish/stream` is a WebSocket that publishes each binary frame it receives as a Message. A frame is the Message frame the subscribe route sends: `TOPIC\rRETAIN\rQOS\rTIMESTAMP\r`, then the bytes LF CR, then the Content. Empty Topic, Retain and QoS fields use the `topic`, `retain` and `qos` query parameters, and the Timestamp is ignored. Only failures are sent back, as JSON (ex. `{"topic":"line1/status","statusCode":403,"message":"..."}`); a frame that can't be parsed fails with 400. Each Message is authorized as a Publish (the `write` Permission on the Service's `publish` operation), the same as `POST /_services/messages/publish`. The route only accepts WebSockets.
- **Working with Streams:** a `TrakHoundStream` that wraps other Streams processes their Items one at a time, in order, and disposes itself when its last input is disposed (set `LeaveInputsOpen` to keep shared inputs running after it is disposed). Exceptions thrown by hooks and subscribers are raised on the `Error` event instead of being thrown. To read a Stream in a loop, use `await foreach (var item in stream.ReadAllAsync(cancellationToken))`.
- **MCP Tools:** `[ApiMcpTool]` on an endpoint offers it to AI clients as a Tool of the Instance's MCP Server (`/_mcp`). Set a `Name` (prefixed with the API's route, ex. `api_orders_get_order`) and a `Description` written for an AI agent. GET and Stream endpoints are read-only; set `ReadOnly = true` on another method that only reads (ex. a POST search), so it's served with the default **Read-only Tools only** setting (Tools that change data are only served when an admin turns that off), or `Destructive = true` on one that deletes data or affects equipment. The Tool's inputs come from the endpoint's route, query and body parameters, so describe them (see **Inputs**). Stream endpoints become Tools that collect values for a number of seconds. Only mark endpoints that are safe for an agent to call. They're served by default once the Instance's MCP Server is on (the API configuration's **MCP Tools** defaults to `Marked`, and **Read-only Tools only** to on). An admin can also serve every endpoint with `All`, which uses your attribute where there is one, so marking endpoints still improves their names and descriptions. Callers need access to the endpoint like any other request.
- **Default route:** `.defaultRoute` in `trakhound.package.json` pre-fills the route when someone adds a configuration.

### 4.2 App

Apps are Blazor Server pages. Each page is a Razor component with `@page`, usually inheriting `TrakHoundComponentBase` (namespace `TrakHound.Blazor`):

```razor
@page "/"
@inherits TrakHoundComponentBase

<h3>Orders</h3>
<p>@count orders</p>

@code {
    private int count;

    protected override async Task OnInitializedAsync()
    {
        var response = await Client.Services.KeyValues.Count("orders");
        count = response.Success ? response.Content : 0;
    }
}
```

- **Routes:** a page's `@page` template is relative to the configuration's `route`, so `@page "/details"` in an App at `orders` serves `/orders/details`.
- **Route and Query Parameters:** work as in any Blazor app, on an Instance and in the debug host. Route Parameters set `[Parameter]` Properties with the same name (any case), support constraints (`{id:int}`, `{id:guid}`, `{at:datetime}` and the others Blazor has; a value that doesn't fit doesn't match the page), optional Parameters (`{id?}`) and catch-alls (`{*path}`), and are URL decoded. A missing optional Parameter sets the Property to its default. Read query values with `[SupplyParameterFromQuery]` (`[Parameter]` isn't needed). On an Instance, literal segments are case-sensitive (unlike the debug host), so `/Orders` doesn't match `@page "/orders"`.
- **Layout:** pages without `@layout` use the App's `Layout` component (a `LayoutComponentBase` named `Layout`, as in the template), in the Instance and in the debug host. Set `@layout` on a page to use a different one.
- **Members:** `Client`, `Volume`, `Variables`, `Logger`, `Session` (the signed-in user's roles), `Parameters`, `GetParameter<T>()`, `GetService<TService>()`, `Navigation` and `Refresh()`.
- **Static files:** files in the project's `wwwroot` are served at `/_packages/<id>/<version>/…`. Build their URLs with `GetLocation("site.css")`.
- **Shared services:** classes that implement `ITrakHoundTransientAppInjectionService`, `ITrakHoundScopedAppInjectionService` or `ITrakHoundSingletonAppInjectionService` (namespace `TrakHound.Apps`) are created for you; their constructors can take the configuration, client, volume and variables. Get one with `GetService<TService>()`.
- **Components:** `TrakHound.Blazor` provides layout, theme and UI components, and uses Radzen.Blazor (ex. `RadzenDataGrid`) for grids (section 10).
- **Theme:** an App shows the Light Theme until a user picks one. To use Dark instead, add `".defaultTheme": "dark"` to `metadata` in `trakhound.package.json`. A theme button saves the choice with `TrakHoundThemeService.SetTheme`, using the Key from `TrakHoundThemeService.GetAppKey(AppId)`.
- **Debugging:** `Program.cs`, `Debug.razor` and `DebugRoutes.razor` host the App locally; run it with `dotnet run` (`http://localhost:5092`). They are left out of the package. They use components from `TrakHound.Debug.Apps`, so they follow the Instance when it changes: `TrakHoundDebugHead` and `TrakHoundDebugScripts` load the Instance's stylesheets, Theme and scripts, and `TrakHoundDebugApp` gives pages what the Instance cascades (`Client`, `Volume`, `Variables`, `Logger` and the `parameters` in `trakhound.debug.json`). `Logger` entries are printed to the console, and an exception on a page is shown with its stack trace.

### 4.3 Function

Derive from `TrakHoundFunction` (namespace `TrakHound.Functions`) and override `OnRun`. The function receives string parameters and returns a response:

```csharp
public class Function : TrakHoundFunction
{
    public Function(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

    protected override Task<TrakHoundFunctionResponse> OnRun(
        IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        Log(TrakHoundLogLevel.Information, "Running..");
        return Task.FromResult(Ok("Completed"));
    }
}
```

- The Instance uses the first `TrakHoundFunction` type in the assembly. It needs a `functionId` in the configuration.
- The Instance creates the Function with the `(configuration)` constructor and then sets `Client`, `Volume` and `Variables` (a Worker works the same way). The older `(configuration, client, volume)` constructor still loads, but is only kept for existing packages.
- **Input Parameters:** describe each one with a `[FunctionParameter]` attribute on the class:

  ```csharp
  [FunctionParameter("days", Description = "How many days of Runs to keep", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "30")]
  [FunctionParameter("table", Description = "The table to clean up", Required = true, Options = new[] { "function_run", "function_log" })]
  [FunctionParameter("apiKey", Description = "The key for the remote service", Secret = true)]
  public class Function : TrakHoundFunction
  ```

  The Admin interface shows a field for each one on the Run tab and in the Job editor (with its description, default, and a list for `Options`). When a Run starts, the Instance adds the `DefaultValue` of each Parameter that isn't passed, and refuses the Run (status code 400, with the reason in the log) when a `Required` Parameter is missing or a value doesn't fit its `Type` (`String`, `Integer`, `Number`, `Boolean`, `DateTime` or `Json`) or `Options`. A `Secret` Parameter is masked when it's entered and isn't saved with the Run's Input. Values are still strings in `parameters`, and a Function can be passed Parameters it doesn't describe. The Parameters are listed in the Function's information (`parameters`) and by the MCP Server's `functions_get` tool.
- Run a Function from the Admin interface (Explorer > Functions > Run), on a schedule (Function > Schedule, or the Schedule Board), or from code with `Client.Functions.Run(functionId, parameters)`.
- **Jobs:** a Job is a named set of Parameters for a Function, saved in the Admin interface (Function > Jobs). A Scheduled Task runs its Job's Parameters, read when each run starts, so editing a Job changes the next run. A Task without a Job runs with no Parameters, so give your Function defaults for missing parameters.
- **Debugging:** the template's `Program.cs` runs the function once and prints the response, including when the Run fails (it then exits with code 1). It applies the `FunctionParameter` defaults and refuses invalid input (400) the same way the Instance does. The input parameters are `input` in `trakhound.debug.json`, or the JSON in the first argument, ex. `dotnet run -- "{\"name\":\"value\"}"`.

### 4.4 Worker

Derive from `TrakHoundWorker` (namespace `TrakHound.Workers`) and override the start and stop hooks. A Worker starts with the Instance and runs until it stops:

```csharp
public class Worker : TrakHoundWorker
{
    public Worker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }

    private CancellationTokenSource _stop;
    private Task _loop;

    protected override Task OnStartAsync()
    {
        _stop = new CancellationTokenSource();
        _loop = Run(_stop.Token);
        return Task.CompletedTask;
    }

    protected override async Task OnStopAsync()
    {
        _stop.Cancel();
        await _loop;
        _stop.Dispose();
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // Do the work, ex. read a device and publish with Client
            }
        }
        catch (OperationCanceledException) { }
    }
}
```

- Use `OnStart` / `OnStop` or their `Async` versions. Report progress with `UpdateStatus(...)`.
- Start your own loop or timer in `OnStart` and end it in `OnStop`, as above. Don't block in `OnStart`: return once the loop is running. `OnStop` runs once per start, when the Worker is stopped or disposed.
- `Volume.CreateListener(path)` raises an event when files in the volume change, for example to reload settings.
- **Template:** `Worker.cs` is the loop above, run every `interval` milliseconds (a configuration parameter, default 5000), with each run's errors logged so one failure doesn't stop the Worker.
- **Debugging:** the template's `Program.cs` starts the worker with the `parameters` in `trakhound.debug.json` and stops it on Ctrl+C.

### 4.5 Service

A Service stores data for the other packages. Derive from `TrakHoundService` (namespace `TrakHound.Services`) and implement one or more service interfaces:

| Interface | Callers use |
| --- | --- |
| `ITrakHoundBlobService` | `Client.Services.Blobs` |
| `ITrakHoundKeyValueService` | `Client.Services.KeyValues` |
| `ITrakHoundMessageService` | `Client.Services.Messages` |
| `ITrakHoundMessageQueueService` | `Client.Services.MessageQueues` |
| `ITrakHoundTableService` | `Client.Services.Tables` |
| `ITrakHoundTimeSeriesService` | `Client.Services.TimeSeries` |

- The constructor must be exactly `(ITrakHoundServiceConfiguration configuration)`. Services get no `Client`.
- Override `CheckAvailability` to report whether the backend is reachable. Return results with `Ok`, `BadRequest` (400), `NotFound` (404), `Conflict` (409), `RouteNotConfigured` (405), `InternalError` (500), `NotAvailable` (503) and `Timeout` (504). Pick the one that says what happened, because Routers use it to decide whether to try a Route's next Target (see **Routers** below):
  - `NotFound`: the backend doesn't have what was asked for.
  - `NotAvailable`: the backend can't be reached, or isn't configured. It was 404 in earlier versions, so check callers that treat 404 as "nothing found".
  - `Timeout`: the backend didn't answer in time.
  - `RouteNotConfigured`: the Service doesn't support the operation (ex. Metrics). Routers always skip it and try the next Target.
- **Template:** `dotnet new trakhound.service` creates a Key Value Service that holds its values in memory. Replace them with calls to your backend. For more complete examples see `modules/TrakHound.Memory.Service` (Key Values and Messages in memory) and `modules/TrakHound.FileSystem.Service` (Blobs on disk).
- **Debugging:** the template's `Program.cs` creates the Service without an Instance and calls each operation.
- **Routers** (Config > Routers) send each Service call to a Service configuration. Your other packages reach Services through their configuration's `routerId`. A Route tries its Targets in order and moves on only for the results a Target falls back on (by default `NotFound`, `NotAvailable`, `Timeout` and `InternalError`), so a `BadRequest` or `Conflict` is returned to the caller.

### 4.6 Identity

An Identity provider signs people in for the Instance's security profiles. Derive from `TrakHoundIdentityProviderBase` (namespace `TrakHound.Security`):

```csharp
public class Module : TrakHoundIdentityProviderBase
{
    public Module(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client,
        ITrakHoundVolume volume, ITrakHoundVariableCollection variables)
        : base(configuration, client, volume, variables) { }

    public override TrakHoundIdentityProviderFeatures Features =>
        TrakHoundIdentityProviderFeatures.Token;

    protected override Task<TrakHoundIdentityResult> OnAuthenticate(
        TrakHoundIdentityContext context)
    {
        var token = context.Request.GetHeader("X-Example-Token");
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(TrakHoundIdentityResult.NoResult());
        // Validate the token, then return Success with the user's identity and roles
        ...
    }
}
```

- `Features` declares how sign-in works: `Token` (a header or query value on each request), `Redirect` (an external sign-in page, with `OnChallenge` and `OnHandleCallback`) and/or `Credentials` (a username and password, with `OnSignIn`). Implement `OnSignOut` if needed.
- Return `TrakHoundIdentityResult.Success(identity)`, `NoResult()` (not for this provider), `Fail(message)` or `Error(message)`. The Instance asks each provider with `Token` in turn and uses the first `Success`, so a `Fail` doesn't stop another provider from accepting the credentials. When none succeeds and one returned `Fail` or `Error`, a protected resource answers 401 `invalid_token` instead of asking the client to sign in. Return `NoResult()` when the request has nothing for your provider, so a request without credentials isn't treated as rejected.
- Examples: `modules/TrakHound.Token.Identity`, `TrakHound.Ldap.Identity` and `TrakHound.Oidc.Identity`. The Instance also has two built in providers that aren't packages: `api-keys` and `users` (usernames and passwords managed on Security > Users). The `users` provider's sign in page is chosen on Security > Users > **Sign In Page**, with the same three settings as the parameters below (it has no Package, so `signInPage` isn't available).
- **Sign in page:** a provider with `Credentials` uses the Instance's Username and Password form unless its configuration's Parameters choose another page. The page only displays the form: it always posts to the Instance, which checks the antiforgery token and applies the delay and lockout, then calls `OnSignIn`. A page that can't be used (it's missing, or an App Page requires signing in) logs a warning to the `security` log and the Instance's form is shown, so a mistake can't stop everyone from signing in. If both are set, `signInRoute` is used.

  | Parameter | Page |
  | --- | --- |
  | `signInProduct`, `signInLogo` | The Instance's form, with a product name and logo. `signInLogo` is a path in the Instance that doesn't require signing in, ex. `_init/assets/logo.svg` |
  | `signInPage` | A static page in the provider's Package : a folder under `dist` (not `dist` itself) with an `index.html`. To package it, add `<Content Include="signin\**" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` to the project |
  | `signInRoute` | A Page in an App (ex. `/sign-in`), which must be public (see [Security Profiles](#securing-app-pages)) |

  A static page's `index.html` uses tokens, which are HTML encoded, so it doesn't need JavaScript. Its other files are served at `{{assets}}/...` without signing in, so don't put anything private in the folder.

  ```html
  <form method="post" action="{{action}}">
    <p class="error" role="alert">{{error}}</p> <!-- Empty when there's no error, so hide it with .error:empty -->
    <input type="hidden" name="{{antiforgeryName}}" value="{{antiforgeryToken}}" />
    <input type="hidden" name="returnUrl" value="{{returnUrl}}" />
    <input type="text" name="username" value="{{username}}" />
    <input type="password" name="password" />
    <button type="submit">Sign In</button>
  </form>
  ```

  An App Page gets the same values as Query Parameters : `action`, `tokenName`, `token`, `returnUrl` and, after a failure, `error` (`invalid`, `locked` or `unavailable`, so the Page chooses the text). Read them with `[SupplyParameterFromQuery]`. The form must be a plain `<form method="post" action="@Action">` with no `@onsubmit`, since a Circuit can't set the Session Cookie. The Page's layout runs before signing in too, so give it one that doesn't call anything that requires signing in, and don't load files from other sites on it, since the token is in its URL.
- <a id="securing-app-pages"></a>**Securing App Pages:** each Page of an App is a Resource named after its class, `app:{appId}:{class}` (ex. `app:my-app:MyApp.Pages.SignIn`), shown on the App's page in the Explorer. To make a Page public when a Profile covers the App with a wildcard (`app:my-app:*`), add a Profile with no `providerId` that names it. Any matching Profile without a provider makes a Resource public, whatever its priority or the other Profiles, so a wildcard in one logs a warning. Check the result on Security > **What is Public?**.

  ```yaml
  id: my-app-public
  description: Pages that don't require signing in
  assignments:
  - id: sign-in
    resourceId: app:my-app:MyApp.Pages.SignIn
  ```
- **Debugging:** the template's `Program.cs` runs the provider without an Instance. `dotnet run -- <token>` authenticates a request with the token in `X-Example-Token`; `dotnet run -- <username> <password>` calls `OnSignIn`. Test real sign-ins on an Instance with Security > Providers > **Test Sign In**.

## 5. Calling the Instance from your package

`Client` reaches the rest of the Instance through the configuration's router. That is the configuration's `routerId`, else its Group's router, else `default`; a Zone with a router overrides all of them. Every call returns a response with `Success` and content; check `Success` before using the content.

| Call | Example |
| --- | --- |
| Blobs | `await Client.Services.Blobs.Upload("reports", "2026-10.pdf", stream, "application/pdf")` |
| Key Values | `await Client.Services.KeyValues.Publish("settings", "theme", "dark")` |
| Messages | `await Client.Services.Messages.Publish("line1/status", "running")` |
| Message Queues | `await Client.Services.MessageQueues.Publish("orders", order)`, `Pull("orders", count: 10)` (see Message Queues below) |
| Tables | `await Client.Services.Tables.Execute(builder.Build())`, `ReadMultiple<T>(TrakHoundTableRequest.Ansi(query))` |
| Time Series | `await Client.Services.TimeSeries.Write(points)`, `Aggregate(query)` (see [Time Series](#time-series)) |
| Functions | `await Client.Functions.Run("send-report", parameters)` |
| Other APIs | `await Client.Api.Post("orders", order)`, `GetJson<T>(route)` |
| Volumes | `await Client.Volumes.UploadFile(volumeId, path, bytes)` |

- **Publishing many Messages:** open a Publish Stream instead of calling `Publish` for each one. `PublishStreamString("line1/status", retain: false, qos: 0)` (also `PublishStreamBytes`, `PublishStreamJson<T>`, and `PublishStream()`, which takes Messages that carry their own Topic, Retain and QoS) returns a Stream that publishes each pushed Item in order. `Push` returns false when a Message isn't published, and `PublishFailed` reports why (`Topic`, `StatusCode`, `Message`). Each Message is authorized and checked against your Zone the same as `Publish`. Over HTTP the Stream is one WebSocket (see section 4.1), so `Push` returns once the Message is sent and failures arrive afterwards through `PublishFailed`. Dispose the Stream when you're done.
- **Message Queues:** each Message is delivered to one consumer. `Pull` and `Subscribe` leave a delivered Message on the Queue until you `Acknowledge` it (removed) or `Reject` it (returned to the Queue, or discarded with `requeue: false` so a Message that always fails doesn't loop). Pass `autoAcknowledge: true` to remove Messages as they are delivered, which loses a Message if your package stops before handling it. `Acknowledge` and `Reject` return a result for each Delivery ID, so check each one's `Success`: a Delivery ID stops working once the connection it was delivered on closes (the Broker then delivers the Message again). A delivered `TrakHoundQueueMessage` has the `Content` (bytes), the `MessageId`, `ContentType` and `Headers` it was published with (set them with `TrakHoundQueuePublishOptions`), and `Redelivered`. `Publish` returns the Message ID (the one you set, or the one the Service created). `Subscribe(queue, prefetch: 10)` limits how many unacknowledged Messages it is sent at once. Pulling an empty Queue returns an empty list. To develop without a broker, use the Memory Service (`TrakHound.Memory.Service`), which has Message Queues with the same behavior.
- **Tables on any database:** your package doesn't need to know which database it is using. See [Tables](#tables-on-any-database) below. `ReadMultiple<T>` and `ReadMultipleString` return an empty list when there are no rows, while `ReadSingle<T>` and `ReadSingleString` return 404. A failed query keeps the Service's status code: 400 for errors in the query, 409 for constraint violations, 503 when the server can't be reached and 504 for a timeout (see the ReadMe of the Service).
- Calls made while handling an API request or App page run as the signed-in caller, so the caller's permissions apply. Calls from Workers and Functions, which have no caller, are trusted.
- **Zones:** an administrator can put your package in a Zone, which limits what it can reach through `Client` to what the Zone's Rules allow (Instance User Guide, section 2.7.1). A denied call returns a 403 response with "Zone Access Denied" (for streams, no stream), so check `Success` and log the failure rather than assuming the call worked. List what your package calls in its ReadMe so administrators can write the Rules. Zones only cover `Client`: code that uses the file system, network or processes directly is flagged on the Zone page but can't be restricted.
- **Code in another process:** a package that starts its own process (ex. a Python or Node.js worker) can let that process call the System API as the package, so its Zone still applies. Call `await Client.CreateModuleToken()` (Apis, Apps, Functions and Workers) and start the process with `token.GetEnvironmentVariables()`: `TRAKHOUND_URL` (the System API, ex. `http://127.0.0.1:8472/`), `TRAKHOUND_MODULE_TOKEN` and `TRAKHOUND_ROUTER_ID`. The process sends the token in an `X-TrakHound-Module-Token` header (or `Authorization: Bearer thm_…`); a .NET process can connect with `TrakHoundHttpClient.FromModuleEnvironment()` (from the `TrakHound.Client` package if the process isn't a package itself, section 10). Calls made with the token are made as your package: its Zone applies (as it does to `Client`) instead of a security profile. Tokens are kept in memory, are only accepted from the same machine, and stop working when the Instance restarts, when your Api is unloaded, after the `lifetime` you pass, or when you call `Client.RevokeModuleToken(token)`. They work for the System API and Apis, not App pages or the MCP Server.

### Tables on any database

A package sends a Table Request, and the Table Service that receives it generates or selects the SQL for its own database (ex. SQL Server, PostgreSQL or SQLite). This also works through a Router or an HTTP Service, where the package can't know the database. Namespace: `TrakHound.Services.Tables` (and `TrakHound.Clients.Services` for the read methods).

Choose in this order:

1. **Typed steps** for creating Tables and writing rows, since these differ the most between databases.
2. **The `ansi` subset** for other queries.
3. **Query Variants** only for SQL that can't be portable, or that you've tuned for one database.

```csharp
[Table("reading", "plant")]
[TableIndex("ix_reading_time", "timestamp desc")]
public class Reading
{
    [PrimaryKey] public string Id { get; set; }
    [Column(Length = 1000, Unicode = true)] public string Note { get; set; }
    public DateTime Timestamp { get; set; }
}

// Steps run in order, in one Transaction by default
var builder = new TrakHoundTableRequestBuilder();
builder.AppendEnsureTable<Reading>();
builder.AppendQuery("delete from \"plant\".\"reading\" where \"timestamp\" < @cutoff;");
builder.Parameters.Add("cutoff", cutoff);
builder.AppendMerge(readings);
var response = await Client.Services.Tables.Execute(builder.Build());

// A read needs only the "read" Permission
var request = TrakHoundTableRequest.Ansi("select * from \"plant\".\"reading\" order by \"timestamp\" desc;", skip: 0, take: 100);
var items = await Client.Services.Tables.ReadMultiple<Reading>(request);
```

| Builder method | What it does |
| --- | --- |
| `AppendEnsureTable<T>()` | Creates the Table and its Indexes if they don't exist. It doesn't change an existing Table's Columns |
| `AppendInsert(items, overwrite)` | Writes rows. `overwrite` deletes rows with the same Primary Key first. With `[IgnoreDuplicateKey]`, rows whose key exists are skipped |
| `AppendMerge(items)` | Updates rows matched by Primary Key and inserts new ones. With `[Update]` Columns, a row is only updated when one of them changed |
| `AppendQuery(query, skip, take)` | Runs an `ansi` Query, or a `TrakHoundTableQueryVariants`. `skip` and `take` page it, and it should have an `order by` |
| `AppendDeleteBatched<T>(where, batchSize)` | Deletes rows in batches until none match, so a long delete doesn't hold one large Transaction. `where` uses `"target"` as the Table's name. Build the Request with `useTransaction: false` |

`Build(useTransaction, readOnly)` makes the Request. Nothing runs if a step can't run on the Service. `Execute` returns a result for each step, and `ExecuteQuery(request)` returns the result sets of the Query steps.

**The `ansi` subset** is SQL that runs unchanged on every Service that lists `ansi` in its Dialects:

- `select`, `insert`, `update`, `delete`, joins, subqueries, CTEs, `group by`, `union all`, window functions, `exists`, `coalesce`, `nullif`, `case`, `cast`, `concat`, and `like` with `escape`.
- Double-quoted identifiers. Quote every identifier and use lowercase names, since names like `trigger` and `start` are reserved words on some databases.
- `@name` Parameters. Pass values as Parameters, never in the SQL text.
- Not in the subset: `top` and paging clauses (use `skip` and `take`), `[brackets]`, `isnull`, `grouping sets`, `merge`, `go`, and comparing a boolean to a number.

A Service can't tell whether SQL is really portable. It trusts the label, so `select top 5` labeled `ansi` runs on SQL Server and only fails elsewhere. Run your package's queries on more than one database (SQLite needs no setup) to catch this.

**Query Variants** hold a version of a Query per Dialect. The Service runs the first of its own Dialects that has one, and `ansi` works as the fallback. Every version uses the same Parameters and must return the same columns. If no version can run, the Service returns 405 and nothing runs, so a Router tries its next Target.

```csharp
var variants = new TrakHoundTableQueryVariants
{
    ["ansi"] = "select ... portable SQL ...",
    ["sqlserver"] = "select ... tuned T-SQL ..."
};
var items = await Client.Services.Tables.ReadMultiple<Reading>(TrakHoundTableRequest.Query(variants));
```

**Scripts** (ex. to create a View) are one `.sql` file per Dialect in a folder, named by Dialect (`sqlserver.sql`, `postgres.sql`, `sqlite.sql`). Send each script as its own Request. It runs without a surrounding Transaction, so it manages its own, and statements that already ran stay applied if a later one fails. Nothing records that a script has run, so it must be safe to run again. Only `sqlserver.sql` is split on `GO` lines.

```csharp
var script = TrakHoundTableQueryVariants.FromDirectory(Path.Combine(AssemblyDirectory, "Scripts", "my_view"));
var response = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.Script(script));
```

**Native SQL:** `ExecuteQuery(sql)` and `ExecuteNonQuery(sql)` with a string run the SQL as given on whichever database receives it. They're for tools, such as the Explorer and the MCP Server, and for a package that only ever uses one database. `GetDialects()` lists what a Service runs.

**Older packages:** `TrakHoundTableQueryBuilder` and the `TrakHoundTables` methods that generate SQL (ex. `GenerateInsert`, `GenerateMerge`) are obsolete. They still work, so packages built against them keep running, but the SQL they generate only runs on SQL Server. To move a package, replace them with `TrakHoundTableRequestBuilder`, and replace `[Column(name, definition)]` with `Length` and `Unicode`, since Table Requests ignore the definition. `TrakHoundTables.GetColumnDataType` returns the SQL Server type name, and `GetColumnType` returns the `TrakHoundTableDataType`.

**Differences to expect:** text comparisons are case-insensitive on a default SQL Server database and case-sensitive on PostgreSQL and SQLite. Index names must be unique within a Schema on PostgreSQL and SQLite, so include the Table's name in them.

**Writing a Table Service:** derive from `TrakHoundSqlTableService` and supply a `TrakHoundSqlDialect`, a connection, Parameters, error Status Codes, and `List` and `GetColumns`. List `ansi` in the Dialect's `SupportedDialects` only if the Service passes `tests/TrakHound.Tables.Conformance.Tests`. A store that isn't SQL can implement `ITrakHoundTableService` directly and leave out `ansi`, but it can't run first-party modules such as `TrakHound.Functions.Api`.

### Time Series

Use a Time Series Service for values recorded over time, such as machine data. Your package doesn't need to know which database stores them (ex. `TrakHound.Timescale.Service` or `TrakHound.InfluxDb.Service`). Use Tables for relational data, or for queries that need a specific database. Namespace: `TrakHound.Services.TimeSeries`.

```csharp
var point = new TrakHoundTimeSeriesPoint
{
    Measurement = "spindle",
    Tags = new() { ["machine"] = "M1" },
    Timestamp = DateTimeOffset.UtcNow,
    Fields = new() { ["speed"] = 8000.0, ["state"] = "RUN" },
    Quality = TrakHoundTimeSeriesQuality.Good
};
await Client.Services.TimeSeries.Write(new[] { point });

var response = await Client.Services.TimeSeries.Aggregate(new TrakHoundTimeSeriesAggregateQuery
{
    Measurement = "spindle",
    Tags = new() { ["machine"] = new[] { "M1" } },
    Fields = new[] { "state" },
    Start = start,
    Stop = stop,
    Interval = TimeSpan.FromHours(1),
    Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState },
    State = "RUN"
});
```

**What to put where:**

- **Tags** identify a Series, such as a machine or a line. Use a small set of known values, since each new combination creates a Series. A value with no limit, such as a part serial number, can badly slow the database, so make it a Field.
- **Fields** hold the values: a double, long, bool or string. A Field keeps its first type within a Measurement, and writing another type returns 400. A whole number can be written to a double Field, but a double can't be written to a long Field, and a whole number too large for a double to hold exactly returns 400. In JSON sent to the System API, a number without a decimal point is a whole number, so a new Field whose first batch only has whole numbers is a long. Write `8000.0` first, or include a decimal value in that batch, if it should be a double. JavaScript's `JSON.stringify(8000.0)` writes `8000`, so send a decimal value or add the Field from a client that keeps the decimal point.
- **Names** start with a letter or underscore, then have only letters, numbers and underscores. Measurements and Tags can have up to 63 characters, and Fields up to 60. Fields and Tags can't be named `time`, `timestamp` or `quality`.
- **Quality** says whether a value can be trusted, and each Field has its own. `Quality` applies to every Field of a Point (it defaults to `Good`), and `Qualities` sets the Fields that differ.
- **No value:** a Field with a null value records that it had no value at that time, such as when a sensor has failed. Its Quality must be `Bad`. A Field that has only had null values has no type until its first value.

| Call | What it does |
| --- | --- |
| `Write(points)` | Writes Points. It's safe to retry. A Point with the same Measurement, Tags and Timestamp replaces only the Fields it supplies and their Qualities. A failed batch may be partly written, so retry the whole batch |
| `Read(query)` | Reads the Points from `Start` (inclusive) to `Stop` (exclusive), with a Result for each Series. `Take` applies to each Series, defaults to 1,000 and is limited to 10,000. A Result has `HasMore` set if `Take` left out Points: to page, use the last Timestamp as the next `Start`. `IncludePrevious` adds the last Point before `Start`, as a starting value |
| `ReadLatest(measurement, tags, fields)` | Reads the newest value of each Field of each Series, with its own Timestamp and Quality. It makes a database query for each Field and can scan a lot of data on some databases, so use a Key Value Service for values a dashboard reads often |
| `Aggregate(query)` | Calculates `Functions` for each `Interval`, with a Result for each Series. `Fields` is required |
| `ListMeasurements()`, `ListSeries(measurement, tags)`, `ListFields(measurement)` | List what has Points. `ListFields` also returns each Field's type (null if it has never had a value) |
| `Delete(measurement, tags, start, stop)` | Deletes Points. Not every database can, so handle a 405 (ex. InfluxDB 3 Core) |

A Result has `Columns` and `Rows`. The first Column is the Timestamp, in UTC. `Read` then has a Column for each Field and one for its Quality, named by `TrakHoundTimeSeriesResult.GetQualityColumn` (ex. `speed` and `speed.quality`). A Quality is null when a Point doesn't have the Field, so a null value with a Quality means the Field had no value at that time. `Aggregate` has a Column for each Field and Function, named by `TrakHoundTimeSeriesAggregateQuery.GetColumn` (ex. `speed_mean`). `ReadLatest` returns `Values` instead, with a Field, Timestamp, Value and Quality for each. Nothing found returns 200 with an empty list.

**Reading many Series:** `Tags` on a Query and on `ReadLatest` can have several values for each Tag, ex. `["machine"] = new[] { "M1", "M2" }`, and select the Series that have one of the values of each Tag. In JSON a Tag can be one string or an array of strings. `TrakHoundTimeSeriesQuery.GetTagFilter(tags)` converts the Tags of one Series. `ListSeries` and `Delete` still take one value for each Tag.

**Aggregates:**

- `Mean`, `Min`, `Max` and `Sum` need a number. `Count`, `First`, `Last` and `DurationInState` work on any Field. Another combination returns 400.
- Intervals are aligned to the Unix epoch, and a Row's Timestamp is the start of its Interval. Only Points from `Start` to `Stop` count.
- `TimeWeightedMean` and `DurationInState` (in seconds) hold a value from its Point until the next Point with that Field. This includes a value from before `Start`, but never goes past `Stop` or the current time. Use them for values that hold until they change, since a plain mean weights each Point the same however long it held.
- A Field's `Bad` values are skipped unless `IncludeBadQuality` is set. Null values are never calculated. For the time weighted Functions, a skipped value or a null value ends the value before it, and the time until the next Point with that Field isn't counted.
- `Fill` decides what an Interval without a value returns: `None` leaves it out, `Null` returns null and `Previous` returns the last value. `Count` is 0 instead of being filled.

**Retention** belongs to the database, so see the ReadMe of the Service.

**AI clients:** the MCP Server has `timeseries_*` Tools to list, read, aggregate and write. There's no Tool that deletes.

**Writing a Time Series Service:** implement `ITrakHoundTimeSeriesService`. Validate Requests with `TrakHoundTimeSeriesValidation`, and build Aggregate Results with `TrakHoundTimeSeriesAggregates`, so the rules match the other Services. Return `RouteNotConfigured` (405) from an operation the database can't do, so a Router tries its next Target. Add your Service to `tests/TrakHound.TimeSeries.Conformance.Tests`, which checks all of this.

## 6. Parameters, secrets, volumes and logging

- **Parameters:** read them with `Configuration.GetParameter("name")` or `GetParameter<int>("retries")`. To list a package's parameters in the Admin interface's configuration form, add `".configurationParameters": "host,port,useSsl"` to the manifest's `metadata`.
- **Secrets:** parameters whose names contain `password`, `token` or `secret`, or end in `key` (but not `name` or `id`), are encrypted in the configuration file when it's saved. `GetParameter` returns them decrypted; a value that can't be decrypted returns `null`.
- **Configuration variables:** a parameter value can reference a variable shared by every configuration in the profile as `${name}` (ex. `directory: ${dataPath}/files`), or an environment variable of the Instance process as `${env:NAME}`. `$${name}` is the literal text `${name}`. Variables are stored in `config/<profile>/variables.config.yaml` (edited in the Admin interface under Configuration > Variables, and included in deployments). They are read when the Instance starts, so restart it to apply a change. `GetParameter` returns the value with references replaced; read parameters through it, not from the `Parameters` dictionary. To read a variable by name, call `GetConfigurationVariable("dataPath")` (Apis, Apps, Functions and Workers), which returns `null` if it isn't defined.
- **Volume:** `ITrakHoundVolume` reads and writes files relative to the package's volume: `ListFiles`, `ReadString`, `ReadJson<T>`, `ReadBytes`, `ReadStream`, `WriteString`, `WriteJson`, `WriteBytes`, `WriteStream`, `DeleteFile` and `CreateListener`.
- **Variables:** (per package, unrelated to configuration variables) `Variables.Set("lastRun", value)` and `Variables.ReadString("lastRun")` keep small values across restarts. They're stored in the `variables` folder beside the Instance's configuration folder (or `TRAKHOUND_VARIABLE_PATH`). Both the Volume and the Variables can be included in a deployment (Instance User Guide, section 2.6).
- **Logging:** `Log(TrakHoundLogLevel.Warning, "message", "code")` in any package, or `Logger.LogWarning(...)` in Apis and Apps. Entries appear in Explorer > Diagnostics > Logs, under a logger named after the package (ex. `api/<packageId>/<route>`).

## 7. Security

The Instance's Security pages decide who can call each API endpoint and open each App page. Your package declares what can be protected; administrators assign access in security profiles.

- **API permissions:** add `[TrakHoundPermission("read")]` (one or more) to an endpoint. A caller needs one of the listed permissions in a matching profile assignment, or `*`.
- **Resource IDs** that profiles assign access to:

| Resource | ID |
| --- | --- |
| API endpoint | `api:<apiId>:<controllerId>:<endpointId>` |
| App page | `app:<appId>:<pageId>` |
| Service operation | `service:<configurationId>:<family>:<operation>` (permission `read`, or `write` for uploads, deletes, publishes and non-queries) |

  Assignments can end in `*` to match a prefix, ex. `api:orders-api:*`. A resource that no profile matches is open.
- **App pages:** wrap parts of a page in `<TrakHoundProtectedView Permissions="...">` to show them only to users with those roles.
- **The caller:** inside an endpoint, `TrakHoundSecurityContext.Current?.Identity` holds the caller's `UserId`, `Name`, `Roles` and provider. `TrakHoundSecurityContext.Current.Authorize(resourceId, permission)` runs your own checks.
- **API keys:** callers send a key created in Security > API Keys as an `X-API-Key: thk_…` header or `Authorization: Bearer thk_…`. A `TrakHoundHttpClient` sends the credentials set on its `TrakHoundHttpClientConfiguration` with every request and WebSocket: `ApiKey`, `BearerToken`, `ModuleToken` (section 5), and any other `Headers`.
- **System API OpenAPI:** `/_instances/openapi` returns an OpenAPI document for the whole System API, `/_instances/openapi/areas` lists its areas (ex. `volumes`, `services`, `functions`), and `/_instances/openapi/<area>` returns the document for one area (resource `system:instance`). Generate clients for other languages from them. Each operation's `x-trakhound-resource` is the System resource and permission it needs, and subscribe routes (WebSockets) are listed in `x-trakhound-streams`.
- **Api Catalog:** developers calling your API from their own applications can discover it without the Admin interface. `/_developer/catalog` lists the APIs served over HTTP with only the endpoints the caller can call, and `/_developer/openapi/<apiId>` returns the matching OpenAPI document (resource `system:api-catalog`). `/llms.txt` is the same information as a Markdown index for AI agents, and the MCP Server offers the OpenAPI documents as resources. The `TrakHound.ApiExplorer.App` package browses the Catalog with Try It requests and code samples. Endpoint and parameter `Description`s (section 4.1) appear there, so write them for the people calling your API.

### 7.1 Licensing a package

A package can require a License. Licenses are checked offline: nothing contacts the publisher.

1. **Create a key pair** with `th licenses keygen contoso`. Keep `contoso.license.private.pem` secret and outside your repository: anyone with it can issue Licenses for your Products.
2. **Embed the public key** (`contoso.license.public.pem`) in your package's code as a constant. Never read it from configuration, or a customer could replace it with their own.
3. **Describe the License** in the package metadata: `.licenseRequired` (`true`), `.licensePublisherId` (ex. `contoso`), `.licenseProductId` (ex. `contoso.widget`) and `.licenseRequestUrl` (where to request one). The Instance's package page links to the request URL with the `productId` and `deviceId` added.
4. **Issue Licenses** for a customer's Device ID with `th licenses sign`, or from your own order system with `TrakHoundLicenseSigner`:

   ```bash
   th licenses sign --key ./contoso.license.private.pem --product contoso.widget --device <device-id> --limit 5 --expires 2027-12-31
   ```

   Choose one of `--expires`, `--days` or `--permanent`. `--any-device` issues a License that works on any Device; its limit then applies to each installation. `th licenses inspect <code> --public-key <file> --product <id>` shows what a code contains and checks it.

   To send several Licenses at once, write a license file with `TrakHoundLicenseFile` (`ToJson`) for the customer to import. Each License can have a `description` (ex. the order number or site it is for), which the Licenses page shows. The description isn't signed, so the customer can change it: never use it to decide what is licensed.

   ```json
   {
     "publisherId": "contoso",
     "deviceId": "<device-id>",
     "licenses": [
       { "productId": "contoso.widget", "licenseCode": "XXXXX-...", "description": "PO-4471, Line 3" }
     ]
   }
   ```
5. **Check the License in your code**, in the code paths that need it:

   ```csharp
   private const string PublicKey = "-----BEGIN PUBLIC KEY-----
...
-----END PUBLIC KEY-----";
   private const string ProductId = "contoso.widget";

   var licenseManager = TrakHoundLicenseManagers.Get("contoso");
   var assignment = licenseManager.AddAssignment(ProductId, licenseManager.GetDeviceId(), connectionId, "Line 3 connection");

   var result = TrakHoundLicenseVerifier.VerifyAssignment(PublicKey, ProductId, licenseManager, assignment.AssignmentId);
   if (!result.IsValid) Log(TrakHoundLogLevel.Warning, $"Not licensed : {result.Message}");
   ```

   The License Manager keeps the Licenses a customer adds on the Licenses page and which Assignments use them, up to each License's limit. It reads License Codes but doesn't check their signatures, so always check with `TrakHoundLicenseVerifier` rather than the Manager's `CheckLicenseCode` or `CheckAssignment` (those are for showing status).
6. **Compile the verifier into your package** so a changed `TrakHound.Common.dll` can't skip the check. `TrakHound.Common` is loaded from the Instance, so `TrakHoundLicenseVerifier` there is only as trustworthy as the Instance's copy. Compile the files from `libraries/TrakHound.Common/Licenses` into your own assembly instead, with `TRAKHOUND_LICENSE_SOURCES` defined. It makes them `internal`, so they don't clash with `TrakHound.Common`'s public types (the compiler uses your copies and warns with CS0436, which you can turn off):

   ```xml
   <PropertyGroup>
     <TrakHoundLicenseSourcePath>$(TrakHoundFrameworkPath)libraries\TrakHound.Common\Licenses\</TrakHoundLicenseSourcePath>
     <DefineConstants>$(DefineConstants);TRAKHOUND_LICENSE_SOURCES</DefineConstants>
     <NoWarn>$(NoWarn);CS0436</NoWarn>
   </PropertyGroup>
   <ItemGroup>
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundBase32.cs" Link="Licenses\TrakHoundBase32.cs" />
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundContainerDetection.cs" Link="Licenses\TrakHoundContainerDetection.cs" />
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundDeviceId.cs" Link="Licenses\TrakHoundDeviceId.cs" />
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundLicenseCode.cs" Link="Licenses\TrakHoundLicenseCode.cs" />
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundLicenseValidationResult.cs" Link="Licenses\TrakHoundLicenseValidationResult.cs" />
     <Compile Include="$(TrakHoundLicenseSourcePath)TrakHoundLicenseVerifier.cs" Link="Licenses\TrakHoundLicenseVerifier.cs" />
   </ItemGroup>
   ```

   Since the types are internal, other assemblies in your package can't use them: wrap the check in a public method of the assembly that compiles them (ex. `bool IsLicensed(string assignmentId)`). `TrakHoundLicenseValidationResult.cs` is only needed while you reference a `TrakHound.Common` package from before signed License Codes, which doesn't have `IsAnyDevice` and `AssignmentLimit`.

A License Code is 141 characters of Crockford Base32 (shown in groups of 5). It holds the Product, the Device, the Assignment Limit and the expiration, signed with ECDSA P-256. Changing any of them makes the signature invalid. To replace a key, list both public keys (`TrakHoundLicenseVerifier.Verify` takes several) until the old Licenses expire.

The Device ID is the SHA-256 of the machine name, as 52 characters of lower case Crockford Base32 (`th licenses device-id` shows it). A container's machine name changes when it is recreated, so in a container `TRAKHOUND_DEVICE_ID` sets a stable value instead. It is only used when `DOTNET_RUNNING_IN_CONTAINER` is `true` and the runtime shows in the file system (`/.dockerenv`, `/run/.containerenv`, `/proc`, or the Windows `ContainerType` registry value); otherwise it is ignored and the Licenses page says why.

## 8. Package file reference

`trakhound.package.json` describes the package:

```json
{
  "id": "Contoso.Hello",
  "category": "api",
  "metadata": {
    "publisher": "Contoso",
    "description": "Says hello",
    ".defaultRoute": "contoso/hello"
  },
  "dependencies": [
    { "id": "TrakHound.Memory.Service", "version": "1.0.0" },
    { "id": "TrakHound.Entities.Api", "version": "1.*", "minimumVersion": "1.2.0" }
  ]
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | Yes | The package ID. The project's assembly must be named `<id>.dll` |
| `category` | Yes | `api`, `app`, `function`, `worker`, `service` or `identity` |
| `version` | Yes, to build | A two part prefix (ex. `"0.3"`). The CLI adds the git height when it builds the package (ex. `0.3.4`, see below) |
| `metadata` | No | `publisher`, `description`, `project`, `repository`, `.defaultRoute`, `.defaultVolumeId`, `.configurationParameters`, `.image` and your own keys. `.defaultVolumeId` sets the Volume ID of a new configuration, and of an existing configuration with an empty Volume ID when a version it runs is installed. That module then uses the new Volume instead of the one named after its configuration ID, and its files aren't moved |
| `dependencies` | No | Packages installed with this one: `id`, `version` and optional `minimumVersion` (see below) |

A dependency's `version` is an exact version (`1.0.0`) or a wildcard (`1.*` matches any 1.x version). Add `minimumVersion` when your package needs a feature added in a later version: `"version": "1.*", "minimumVersion": "1.2.0"` accepts 1.2.0 or newer, but not 2.x. An installed dependency that matches is kept. Otherwise the latest matching version is downloaded, and the install fails if that version is still below the minimum. Packing fails if `minimumVersion` isn't a valid version, or if it's higher than an exact `version`. Older Instances ignore `minimumVersion` and only match `version`.

The CLI adds `trakhoundVersion` (the TrakHound version the package was built against), `buildDate`, a hash and a `contentHash` when it packs. A `Readme.md` (or `Readme.txt`) and a `License` file in the package's `dist` folder are shown with the package. Set them to copy to the output directory so the build includes them, and name the Readme `Readme.md` in the output (the Instance reads it by that name, which matters on Linux): `<None Update="README.md" TargetPath="Readme.md" CopyToOutputDirectory="Always" />`. The CLI also copies the Readme to the root of the package, where package servers (ex. packages.trakhound.com) read it. Use absolute links in it, since relative links don't work there.

**Versions.** The manifest's `version` is a two part prefix you set by hand (ex. `"0.3"`). The CLI adds the git height when it builds the package: the number of commits in the repository, including the commit itself, since the `version` line last changed. So `"0.3"` four commits later builds `0.3.4`. Change the prefix to start a new version line (it's `0.4.0` until the change is committed). The same commit always builds the same version, on any machine, and the CLI never writes a version back to a file. The CLI needs the full git history, so it stops in a shallow clone.

`trakhound.package.publish.json` sets where packages are published, as named **Publish Targets**:

```json
{
  "defaultTarget": "development",
  "targets": {
    "development": {
      "destinations": [ { "managementServer": "http://packages.contoso.local:8472", "organization": "Contoso" } ],
      "versionSuffix": "dev{branch}.{timestamp}",
      "allowDirty": true
    },
    "release": {
      "destinations": [ { "managementServer": "https://packages.contoso.com", "organization": "Contoso", "apiTokenVariable": "CONTOSO_RELEASE_TOKEN" } ],
      "requireSigning": true
    }
  }
}
```

- `destinations` lists the package servers and organizations to upload to. `apiTokenVariable` names the environment variable that holds that server's Api Token (otherwise the CLI's `apiToken` is used, section 9).
- `versionSuffix` adds a prerelease label to every version published to the Target. `{timestamp}` is the UTC time of the run and `{branch}` is empty on `main` and `-[branch]` on other branches, so development builds are `0.3.4-dev.20261114153000`. A Target without one publishes stable versions (`0.3.4`).
- `allowDirty` lets the Target build uncommitted changes (it needs `{timestamp}` in its `versionSuffix`, so each build has its own version). Other Targets publish the version of the commit, so they refuse uncommitted changes.
- `requireSigning` requires a signing key (section 9) and a clean working tree.
- `--target` (ex. `th packages publish --target release`) picks the Target. Without it, `defaultTarget` is used.

The CLI uses the nearest `trakhound.package.publish.json`, looking in the package folder first and then in each parent folder, so one file at the root of a repository covers every package in it. The nearest file is used as a whole. A package only needs its own file to publish somewhere different from the rest. Without any file, `--server` and `--organization` set where to publish.

## 9. Building and publishing

`th packages dotnet [path]` builds a project and packs it. Its options:

| Option | Default | Meaning |
| --- | --- | --- |
| `--output`, `-o` | Current folder | Where to write the `.thp` file. Not written when publishing without this option |
| `--project-configuration` | `Release` | The build configuration |
| `--include-debug-symbols` | `true` | Include `.pdb` files |
| `--include-source` | `false` | Include the source as `src.zip` |
| `--publish` | `false` | Upload to the Publish Target's destinations (section 8) |
| `--target`, `-t` | `defaultTarget` | The Publish Target to upload to (with `--publish`) |
| `--version`, `-v` | Prefix and git height | The package version |
| `--version-suffix` | The Target's `versionSuffix` | A prerelease label added to the version (ex. `dev.{timestamp}` gives `0.3.14-dev.20261111224029`) |
| `--server`, `-s` / `--organization` | | Where to upload, for a package without a `trakhound.package.publish.json` |
| `--skip-existing` | `false` | Skip a destination that already has the version when it was published from the same commit (see below) |

Options also work in camel case (`--projectConfiguration`). The version is `--version` if set, otherwise the manifest's prefix and the git height (section 8). Before uploading, the CLI reads the versions on each of the Target's destinations: it stops if the version is already published (unless `--skip-existing` applies), and publishes nothing if a destination can't be read (ex. an unreachable server), so a package goes to all of them or none. The Target is checked before building too: its Api Tokens, its signing key (`requireSigning`) and uncommitted changes (`allowDirty`).

**Prereleases:** a Target's `versionSuffix`, or `--version-suffix` (also on `pack`, `publish` and `build`), adds a prerelease label to the version, so `0.3.14` built for development is `0.3.14-dev.20261111224029`. Every prerelease of `0.3.14` comes before the `0.3.14` release. A version is never replaced, so the CLI stops if the version with its label is already published: use `{timestamp}` (ex. `dev.{timestamp}`) or a label with a number (ex. `beta.2`). A `--version` that already has a label (ex. `1.0.0-beta`) is used as is. Instances select a prerelease only when a version asks for one (ex. `*-*`) or when no stable version matches, and the package page marks it as a prerelease.

**Publishing again:** with `--skip-existing` (also on `publish` and `build`), a destination that already has the version is skipped instead of stopping, when that package was built from the same commit without uncommitted changes (its `repositoryCommit` and `repositoryDirty`). It's the same source, so there's nothing new to upload, ex. after a `build` that stopped partway. The working tree must be that clean commit, and a version published from another commit is still an error. `build` reports the project as `ALREADY PUBLISHED`, and the packages that depend on it are still published.

**What the build does:** it temporarily turns the project into a library (`EnableDynamicLoading`), removes the TrakHound debug references and the debug host files, publishes it to a `dist` folder, and zips that with a `package.json` manifest into `<id>-<version>.thp`. Your project file is restored afterwards.

**Source and file checks:** every package contains a `package.manifest.json` that lists each file with its SHA-256, plus the package's identity and source. `th packages dotnet` records the git branch and commit, and whether the working tree had uncommitted changes (it warns if so). Set `repository` in the metadata so the commit can be traced to a repository. `th packages verify` compares a package with its manifest:

```bash
th packages verify --file ./MyPackage-1.0.0.thp
```

Without `--file` it checks the installed packages. It lists changed, missing and added files, and exits with an error if a package fails or has no manifest. `--include-source` leaves `.git` and `trakhound.debug.json` out of `src.zip`.

**Signing:** a signature shows who published a package and stops someone from changing the files and rewriting the manifest to match. Create a key once, and keep the private key out of your repository:

```bash
th packages keygen contoso --output ~/.trakhound/keys
```

Then pass it when you pack, or set the `TRAKHOUND_SIGNING_KEY` environment variable (to the key's path or the key itself, for CI). `--sign-key` also works with `pack`, `publish` and `build`:

```bash
th packages dotnet --sign-key ~/.trakhound/keys/contoso.private.pem
```

The build stops if the working tree has uncommitted changes, because the package wouldn't match the recorded commit. `--allow-dirty` signs anyway and records that it did. The key can't be inside the folder being packed.

Whoever installs the package trusts your public key, per configuration profile:

```bash
th trust add ./contoso.public.pem --name Contoso --prefix Contoso.
```

`--prefix` limits you to package IDs that start with that text, so that you can't sign a package that claims another publisher's ID. After that, `th packages verify` shows the publisher, and `--require-signature` fails any package without a trusted signature. A package signed by a key that isn't trusted is reported as untrusted: without the key, its signature can't be checked. An Instance with `packageVerification` set to `Require` only uses signed packages from its trusted publishers, so sign your packages for those Instances, including when you test them there with `th dev`.

**Checking a package against its source:** `th packages dotnet` builds so that the same commit gives the same files, whatever folder it's built in. Anyone can then rebuild a package and compare:

```bash
th packages rebuild ./MyPackage-1.0.0.thp
```

It clones the recorded `repository`, checks out the commit, builds the project in `repositoryDirectory` and lists any files that differ, exiting with an error if they do. `--repository` and `--project` override the recorded values, ex. to use a local clone. Building runs the repository's code, so the package must be signed by a trusted publisher unless you pass `--allow-untrusted`.

- **Apps:** build them with `--include-debug-symbols false` to be able to check them. The compiler embeds what Razor generates in the symbols, including full paths, so an App with symbols only matches when rebuilt in the same folder. Signing an App with symbols warns about this.
- **Compare like with like:** use the same .NET SDK as the original (it's in the manifest as `dotnetSdkVersion`, and `rebuild` warns if it differs) and the same operating system and line ending settings. Only Windows builds with the same SDK have been compared so far.
- **NuGet versions:** a floating version (`1.*`) can resolve differently later, which shows as a difference. Use exact versions in projects you want to check. A signed build also stops if it can't find the TrakHound version the project references.
- `src.zip` is compared by its contents, and the `Last-Modified` headers in an App's static asset list are ignored, because they only record when the files were checked out.

`th packages dotnet` also adds `sbom.cdx.json`, a CycloneDX list of the NuGet packages and versions the package was built with. It's covered by the manifest and signature. Set the `TRAKHOUND_BUILDER` environment variable to record what built the package, ex. the URL of a CI run.

**Reading manifests from a package server:** IT tools can see who signed a package without downloading it, using the `TrakHound.Packages.Api` module:

| Endpoint | Returns |
| --- | --- |
| `GET /packages/{organization}/{id}/{version}/manifest` | The manifest and signature of a version. A version with a `*` wildcard returns the version an Instance would select (the highest stable match, else the highest prerelease; with `-*` the highest of either) |
| `POST /packages/{organization}/manifests` | The same for up to 200 package UUIDs, sent as a JSON array. A package that isn't found is left out |

The package server stores prereleases too: `versions` lists them in semantic versioning order, and `latest` is the highest stable version, or the highest prerelease if there's no stable version. See the [TrakHound.Packages.Api README](../modules/TrakHound.Packages.Api/README.md).

`manifest` and `signature` are Base64 of the exact bytes in the package, because a signature only matches those bytes. The server doesn't check them, so check them with your own list of trusted keys. In .NET, use `TrakHoundPackageClient.GetManifests`, then `TrakHoundPackageVerifier.VerifyListedManifest`, which also checks that the manifest matches what the server lists. Its result can be `Trusted`, but never `Verified`: only `TrakHoundPackageVerifier.VerifyArchive` compares the files, which needs the whole package.

TrakHound itself is only referenced to compile: the Instance provides `TrakHound.Common` (and `TrakHound.Blazor`) and their dependencies at runtime, so they aren't copied to `dist`. A project that references TrakHound from NuGet is built against that version. A project that references the TrakHound projects (ex. in the TrakHound repository, directly or through a debug project) is built against those projects, and `trakhoundVersion` is their version.

**Other commands:**

| Command | Purpose |
| --- | --- |
| `th packages publish [path]` | Publish an already-packed package directory |
| `th packages push <file.thp>` | Upload a `.thp` file to a package server |
| `th packages install <id>` / `--file <file.thp>` | Install from a package server or a file into `<path>/packages` (`-p`). `--configure` also adds the default configuration for each package, as the Admin interface does, and `--group <id>` puts those configurations in a Group |
| `th packages uninstall <id>` | Remove an installed package |
| `th packages pack [path]` | Pack a folder that isn't a .NET project |
| `th packages verify [id]` / `--file <file.thp>` | Check that a package's files match its manifest, and who signed it |
| `th packages rebuild <file.thp>` | Build a package's recorded commit again and compare the files |
| `th packages keygen [name]` | Create a key pair for signing packages |
| `th trust list\|add\|remove` | Manage the publishers a configuration profile trusts |

**Signing in to a package server:** the CLI reads `managementServer`, `organization` and `apiToken` from `cli.config.yaml` (next to `th.exe`, then `~/.trakhound/cli.config.yaml`) or from the `TRAKHOUND_MANAGEMENT_SERVER`, `TRAKHOUND_ORGANIZATION` and `TRAKHOUND_API_TOKEN` environment variables.

**Visual Studio:** with the TrakHound extension, right-click the project in Solution Explorer (or open the Tools menu) and choose **Publish TrakHound Package**. It runs `th packages dotnet --project-configuration Release --publish` and shows the output in the **TrakHound** output pane. To publish several projects together, choose **Publish TrakHound Packages...** from the same menus: it finds the `trakhound.build.json` files in and near the solution folder, lets you pick which of the listed projects to include, and runs `th packages build` for them. Projects are built in dependency order, and nothing is published unless every selected project builds.

## 10. Libraries

| NuGet package | Use it for |
| --- | --- |
| `TrakHound.Common` | Everything a package needs: base classes, `ITrakHoundClient`, volumes, logging, security. Referenced by the Function, Worker, Service and Identity templates |
| `TrakHound.Blazor` | App base classes (`TrakHoundComponentBase`, layouts), themes, and UI components such as tabs, tree views and modals. Uses Radzen.Blazor |
| `TrakHound.Blazor.Hosting` | The CSS, fonts and scripts an App host loads |
| `TrakHound.Hosting` | Runs Workers, Functions and Services outside of an Instance (section 2.10): `AddTrakHound`, `AddTrakHoundWorker<T>`, `RunTrakHoundFunction<T>`, `AddTrakHoundService<T>`, the Instance health check, and `TrakHoundModuleFactory`, which creates modules the same way the Instance does. Used by `TrakHound.Debug` and `TrakHound.Testing` |
| `TrakHound.Hosting.AspNetCore` | Runs an Api in an ASP.NET Core app (section 2.10): `MapTrakHoundApi` creates its controllers and maps their endpoints and streams the same way the Instance does, on a route group so conventions such as `RequireAuthorization()` apply to every endpoint. `MapTrakHoundOpenApi` serves its OpenAPI document, `AddTrakHoundApiKeyAuthentication` checks API keys, and `MapTrakHoundFunction<T>` runs a Function over HTTP. Used by `TrakHound.Debug.AspNetCore` and the `trakhound.host` template |
| `TrakHound.Debug` | Local debug host (`TrakHoundDebugHost`): reads `trakhound.debug.json` and runs Functions, Workers, Services and Identity providers without an Instance. Used by every template |
| `TrakHound.Debug.AspNetCore` | Local debug host for APIs (`MapTrakHoundApiEndpoints`, which also serves the Api Debugger at `/_debug`). Used by the Api template |
| `TrakHound.Debug.Apps` | Local debug host for Apps (`AddTrakHoundDebugHosting`, `TrakHoundDebugHead`, `TrakHoundDebugApp`, `TrakHoundDebugScripts`). Used by the App template |
| `TrakHound.Testing` | Unit tests without an Instance: `TrakHoundTestContext` and an in-memory `TrakHoundTestClient` (section 2.9). Reference it from a test project, not the package project |
| `TrakHound.Client` | Calls an Instance from an app that isn't a package, including .NET Framework 4.7.2+ apps (it targets .NET Standard 2.0 and .NET 10): the same `ITrakHoundClient` and `TrakHoundHttpClient` as `TrakHound.Common`, without the package code. It has its own repository ([TrakHound.Client](https://github.com/TrakHound/TrakHound.Client)). Don't reference it from a package, or alongside `TrakHound.Common`: they contain the same types |

The build removes the debug packages when it packs, and the Instance provides `TrakHound.Common` and `TrakHound.Blazor` at run time.

## 11. Troubleshooting

| Problem | What to do |
| --- | --- |
| The template doesn't compile (missing `TrakHound.Variables`, `ApiGet`, `TrakHoundWorker`) | The project references a TrakHound version older than the template code, ex. one created with `--trakhoundVersion "*"`. Change the TrakHound version in the `.csproj` to your Instance's version (section 2.2) |
| The API returns 404 after you add a configuration | Open Explorer > Diagnostics > Modules. "Could not load file or assembly 'TrakHound.Common…'" means the package was built against a newer TrakHound than the Instance (section 3.1). "Assembly Not Found" means the assembly isn't named `<id>.dll` |
| "No … Types Found" in Modules | The assembly has no public, non-abstract class of the right base type |
| Saving a configuration says the package "could not be downloaded" | The Instance doesn't know about the installed package. Restart the Instance after installing with the CLI, or install through Packages > Upload |
| `th packages dotnet` fails to find the TrakHound version | Restore or build the project once first (`dotnet build`) |
| Publishing fails to sign in | Set `apiToken` in `cli.config.yaml` or `TRAKHOUND_API_TOKEN`, or the environment variable a destination's `apiTokenVariable` names in `trakhound.package.publish.json` |
| `Client` calls return 403 "Zone Access Denied" | The package is in a Zone that doesn't allow the call. Ask the administrator to grant it from the Zone's Activity, or to use Audit mode while developing (section 5) |
| `Client` calls fail in the debug host | The debug host connects to `instanceUrl` in `trakhound.debug.json` (default `http://localhost:8472`) and prints "Instance Not Reachable" when it can't. Start that Instance, change the URL, or set `apiKey` if the Instance requires one |
