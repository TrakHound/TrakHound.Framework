# UserPackageId

This project is an **Api** package: HTTP endpoints that a TrakHound Instance serves at the route of each configuration that runs it. Its package ID is `UserPackageId` (category `api`).

Developer guide: https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md

## Commands

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Run the debug host (http://localhost:5091, Api Debugger at /_debug) | `dotnet run` |
| Build the package file (`.thp`) | `th packages dotnet --output ./out` |
| Publish to the Publish Target in the nearest `trakhound.package.publish.json` | `th packages dotnet --publish [--target <name>]` (or `publish-package.bat` / `.sh`) |

`th` is the TrakHound CLI. The version is the `version` prefix in `trakhound.package.json` plus the git height (the commits since the prefix changed), so commit before publishing. Change the prefix to start a new version line.

## Files

| File | Purpose |
| --- | --- |
| `Controller.cs` | The endpoints (a `TrakHoundApiController`) |
| `Program.cs` | Local debug host (left out of the package) |
| `trakhound.debug.json` | The debug host's settings: `instanceUrl`, `apiKey` and the configuration `parameters` (left out of the package). Keep real credentials out of source control |
| `Properties/launchSettings.json` | The launch profile for `dotnet run`, Visual Studio and VS Code (port 5091, Development environment, opens the Api Debugger) |
| `.vscode/` | VS Code: the launch configurations (the debug host, and Attach to Instance for `th dev run`), tasks (build, Build package, Publish package, Dev run, Dev clean), the recommended extension (C# Dev Kit) and the Instance's MCP Server (`mcp.json`) |
| `trakhound.package.json` | The package manifest: `id`, `category`, `metadata` (publisher, description, ...) and `dependencies` |
| `trakhound.package.publish.json` | Where to publish : the Publish Targets (only created when a package server was given; otherwise the nearest one in a parent folder is used) |
| `README.md` | What the package does and how to configure it, shown with the package on the Instance and the package server |
| `UserPackageId.csproj` | The project. The assembly must stay named `UserPackageId.dll` (the package ID) |

## Writing endpoints

- Derive from `TrakHoundApiController` (namespace `TrakHound.Api`). The Instance creates each controller with its parameterless constructor, sets its members, then calls `OnInitialize` / `OnInitializeAsync`.
- Endpoint attributes: `[ApiGet]`, `[ApiPost]`, `[ApiPut]`, `[ApiPatch]`, `[ApiDelete]`, `[ApiHead]` and `[ApiStream]`, each with an optional route relative to the Api's route (ex. `[ApiGet("orders/{id}")]`). `[TrakHoundApiController("orders")]` on the class puts its endpoints under `<route>/orders`.
- Inputs: `[FromQuery]`, `[FromRoute]`, `[FromHeader("X-Name")]` and `[FromBody(ContentType = "application/json")]`. Parameters with a default value are optional. Header names are case-insensitive. Headers with the caller's credentials for the Instance (`Cookie`, `Authorization`, `Proxy-Authorization`, `X-API-Key`, `X-TrakHound-Module-Token` and `token`) are never passed to an Api, so authenticate your own callers with another header.
- Return `Ok`, `Created`, `Accepted`, `File`, `Redirect`, `BadRequest`, `NotAuthorized`, `NotFound` or `InternalError`, with nothing, a string (text/plain), an object (JSON) or bytes and a content type.
- Streams: an `[ApiStream]` endpoint returns `ITrakHoundStream<TrakHoundApiResponse>`; push values with `TrakHoundStream<T>.Push` and stop any timers when the stream is disposed. Clients connect with a WebSocket or Server-Sent Events.
- Security: `[TrakHoundPermission("read")]` on an endpoint lets administrators require that permission. Inside an endpoint, `TrakHoundSecurityContext.Current?.Identity` is the caller.

### Describe every endpoint and parameter

Descriptions appear in the Instance's Api information, its OpenAPI documents, the Api Catalog and the MCP Server's tools for AI clients, so write them for someone calling the Api (say what it returns, what is required, defaults, limits and units). Check them in the debug host's Api Debugger (`/_debug`): it shows the OpenAPI document the Instance creates. Keep each description on one line (don't split it with `+`), and put each described parameter on its own line:

```csharp
[ApiGet("orders", Description = "Lists the orders that were placed between start and stop, newest first.")]
public async Task<TrakHoundApiResponse> GetOrders(
    [FromQuery(Description = "The earliest order time to include (ISO 8601)")] string start,
    [FromQuery(Description = "The most orders to return (up to 1,000)")] int take = 100
    )
```

### Offering endpoints to AI clients (MCP)

Mark an endpoint with `[ApiMcpTool]` to offer it as a Tool of the Instance's MCP Server (`/_mcp`):

```csharp
[ApiGet("orders/{id}", Description = "Gets an order.")]
[ApiMcpTool(Name = "get_order", Description = "Gets an order by its ID, with its lines and status")]
```

- Only mark endpoints that are safe for an AI agent to call. GET and Stream endpoints are read only; set `ReadOnly = true` on another endpoint that only reads (ex. a POST search) and `Destructive = true` on one that deletes data or affects equipment.
- By default an Instance only serves read-only Tools, and only the endpoints marked with `[ApiMcpTool]`.
- The Tool's inputs come from the endpoint's parameters, so their descriptions matter.

## Calling the Instance

`Client` reaches the rest of the Instance through the configuration's router:

- `Client.Services.Blobs`, `KeyValues`, `Messages`, `MessageQueues`, `Tables` and `TimeSeries`; `Client.Functions.Run(...)`; `Client.Api.GetJson<T>(route)` / `Post(...)` for other Apis; `Client.Volumes`.
- Every call returns a response with `Success`. Check it before using the content, and log the failure. Don't assume a call worked.
- An administrator can put the package in a **Zone**, which limits what it can reach through `Client`. A denied call returns 403 "Zone Access Denied". List what the package calls in its `README.md` so administrators can allow it.

## Parameters, storage and logging

- Parameters: `Configuration.GetParameter("name")` or `GetParameter<int>("retries")`. Always read them through `GetParameter` (it resolves `${variable}` references and decrypts secrets). List them for the Admin interface's form with `".configurationParameters": "host,port"` in the manifest's `metadata`.
- Parameters named like `password`, `token`, `secret` or ending in `key` are encrypted when saved. Never log their values.
- `Volume`: the package's file storage (`ReadJson<T>`, `WriteJson`, `ListFiles`, `DeleteFile`, ...). Each configuration has its own volume.
- `Variables.Set` / `ReadString`: small values kept across restarts.
- `Log(TrakHoundLogLevel.Warning, "message")`: shown in the Instance's log history under the package's logger.

## Unit tests

Put tests in a separate test project that references this project and the `TrakHound.Testing` NuGet package (same version as `TrakHound.Common`); don't reference `TrakHound.Testing` from this project. Create a `TrakHoundTestContext` per test: it creates modules the way the Instance does (`RunFunction<T>`, `CreateWorker<T>`, `CreateApiController<T>`, `CreateService<T>`, `CreateIdentityProvider<T>`), with configuration `Parameters` you set, a temporary Volume and Variables, and the logged entries in `Logs`. Its `Client` keeps `Services.KeyValues` and `Services.Blobs` in memory, runs Functions you register with `RegisterFunction<T>(id)`, and throws `NotSupportedException` for anything else until you set that property to a fake. Developer guide section 2.9.

## Rules

- **TrakHound version:** reference the TrakHound version of the Instance the package will run on, or older. The Instance provides `TrakHound.Common` (and `TrakHound.Blazor`) at run time, so a package built against a newer version fails to load. Don't copy TrakHound assemblies into the package.
- **Package ID:** don't rename the project or assembly without changing `id` in `trakhound.package.json` to match.
- **Debug host:** the debug host files are left out of the package by `th packages dotnet`. Don't put package code in them.
- **Readme:** `README.md` is copied to the package as `Readme.md` and shown with the package on the Instance and on the package server (ex. packages.trakhound.com). Write it for the people who install the package: what it does, its configuration parameters, and what it calls through `Client`. Replace the placeholder text, keep its tables the same as the code, and use absolute links (`https://...`), since relative links and HTML don't work there.
- **Source control:** `bin`, `obj`, the debug `volume` folder and `.thp` files stay out of it (see `.gitignore`).

## Troubleshooting

| Problem | Cause |
| --- | --- |
| Doesn't load on the Instance ("Could not load file or assembly 'TrakHound.Common'") | Built against a newer TrakHound than the Instance |
| "Assembly Not Found" or "No ... Types Found" (Explorer > Diagnostics > Modules) | The assembly isn't named after the package ID, or has no public, non-abstract class of the right base type |
| `Client` calls fail in the debug host ("Instance Not Reachable") | The debug host connects to `instanceUrl` in `trakhound.debug.json` (default `http://localhost:8472`); start that Instance, or set `apiKey` if it requires one |
| `Client` calls return 403 "Zone Access Denied" | The package's Zone doesn't allow the call |
