# UserPackageId

This project is an **App** package: Blazor Server pages that a TrakHound Instance serves at the route of each configuration that runs it. Its package ID is `UserPackageId` (category `app`).

Developer guide: https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md

## Commands

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Run the debug host (http://localhost:5092) | `dotnet run` |
| Build the package file (`.thp`) | `th packages dotnet --output ./out` |
| Publish to the Publish Target in the nearest `trakhound.package.publish.json` | `th packages dotnet --publish [--target <name>]` (or `publish-package.bat` / `.sh`) |

`th` is the TrakHound CLI. The version is the `version` prefix in `trakhound.package.json` plus the git height (the commits since the prefix changed), so commit before publishing. Change the prefix to start a new version line.

## Files

| File | Purpose |
| --- | --- |
| `Pages/` | The App's pages (Razor components) |
| `_Imports.razor` | Usings for the pages |
| `Layout.razor` | The layout the pages use |
| `Program.cs, Debug.razor, DebugRoutes.razor` | Local debug host (left out of the package). Uses `TrakHoundDebugHead`, `TrakHoundDebugApp` and `TrakHoundDebugScripts` from `TrakHound.Debug.Apps`, which match what the Instance gives App pages |
| `trakhound.debug.json` | The debug host's settings: `instanceUrl`, `apiKey`, the configuration `parameters` and the `theme` (`light` or `dark`) (left out of the package). Keep real credentials out of source control |
| `Properties/launchSettings.json` | The launch profile for `dotnet run`, Visual Studio and VS Code (port 5092, Development environment) |
| `.vscode/` | VS Code: the launch configurations (the debug host, and Attach to Instance for `th dev run`), tasks (build, Build package, Publish package, Dev run, Dev clean), the recommended extension (C# Dev Kit) and the Instance's MCP Server (`mcp.json`) |
| `trakhound.package.json` | The package manifest: `id`, `category`, `metadata` (publisher, description, ...) and `dependencies` |
| `trakhound.package.publish.json` | Where to publish : the Publish Targets (only created when a package server was given; otherwise the nearest one in a parent folder is used) |
| `README.md` | What the package does and how to configure it, shown with the package on the Instance and the package server |
| `UserPackageId.csproj` | The project. The assembly must stay named `UserPackageId.dll` (the package ID) |

## Writing pages

- Each page is a Razor component with `@page`, usually `@inherits TrakHoundComponentBase` (namespace `TrakHound.Blazor`).
- A page's `@page` route is relative to the configuration's route: `@page "/details"` in an App at `orders` serves `/orders/details`.
- Members: `Client`, `Volume`, `Variables`, `Logger`, `Session` (the signed-in user's roles), `Parameters`, `GetParameter<T>()`, `GetService<TService>()`, `Navigation` and `Refresh()`.
- Static files in `wwwroot` are served at `/_packages/<id>/<version>/...`; build their URLs with `GetLocation("site.css")`, never hard-coded paths.
- Shared services: classes that implement `ITrakHoundTransientAppInjectionService`, `ITrakHoundScopedAppInjectionService` or `ITrakHoundSingletonAppInjectionService` (namespace `TrakHound.Apps`) are created for you; get one with `GetService<TService>()`.
- UI: `TrakHound.Blazor` has layout, theme and UI components, and uses Radzen.Blazor (ex. `RadzenDataGrid`).
- Security: wrap parts of a page in `<TrakHoundProtectedView Permissions="...">` to show them only to users with those roles.

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
