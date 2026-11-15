# UserPackageId

This project is a **Service** package: a storage backend (Blobs, Key Values, Messages, Message Queues, Tables or Time Series) that other TrakHound packages use through `Client.Services`. Its package ID is `UserPackageId` (category `service`).

Developer guide: https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md

## Commands

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Create the Service and call each operation | `dotnet run` |
| Build the package file (`.thp`) | `th packages dotnet --output ./out` |
| Publish to the Publish Target in the nearest `trakhound.package.publish.json` | `th packages dotnet --publish [--target <name>]` (or `publish-package.bat` / `.sh`) |

`th` is the TrakHound CLI. The version is the `version` prefix in `trakhound.package.json` plus the git height (the commits since the prefix changed), so commit before publishing. Change the prefix to start a new version line.

## Files

| File | Purpose |
| --- | --- |
| `Service.cs` | The Service (a `TrakHoundService`); the template is a Key Value Service that holds its values in memory |
| `Program.cs` | Local debug host that creates the Service and calls each operation (left out of the package) |
| `trakhound.debug.json` | The debug host's settings: `instanceUrl`, `apiKey` and the configuration `parameters` (left out of the package). Keep real credentials out of source control |
| `trakhound.package.json` | The package manifest: `id`, `category`, `metadata` (publisher, description, ...) and `dependencies` |
| `trakhound.package.publish.json` | Where to publish : the Publish Targets (only created when a package server was given; otherwise the nearest one in a parent folder is used) |
| `UserPackageId.csproj` | The project. The assembly must stay named `UserPackageId.dll` (the package ID) |

## Writing the Service

- Derive from `TrakHoundService` (namespace `TrakHound.Services`) and implement one or more of `ITrakHoundBlobService`, `ITrakHoundKeyValueService`, `ITrakHoundMessageService`, `ITrakHoundMessageQueueService` and `ITrakHoundTableService`, `ITrakHoundTimeSeriesService`.
- The constructor must be exactly `(ITrakHoundServiceConfiguration configuration)`. Services get no `Client`, so a Service can't call other packages.
- Override `CheckAvailability` to report whether the backend is reachable.
- Return results with `Ok`, `BadRequest`, `NotFound`, `Conflict`, `NotAvailable` and `InternalError`; don't throw for expected failures.
- Every operation can be called concurrently: make the Service thread safe.
- For a Table Service on a SQL database, derive from `TrakHoundSqlTableService` and supply a `TrakHoundSqlDialect` (Developer Guide, "Tables on any database"). List `ansi` in its `SupportedDialects` only if the Service runs the documented `ansi` subset unchanged.
- Reference implementations in the TrakHound.Framework repository: `modules/TrakHound.Memory.Service` (Key Values and Messages in memory), `modules/TrakHound.FileSystem.Service` (Blobs on disk) and `modules/TrakHound.Sqlite.Service` (Tables).

## Calling the Instance

`Client` reaches the rest of the Instance through the configuration's router:

- `Client.Services.Blobs`, `KeyValues`, `Messages`, `MessageQueues`, `Tables` and `TimeSeries`; `Client.Functions.Run(...)`; `Client.Api.GetJson<T>(route)` / `Post(...)` for other Apis; `Client.Volumes`.
- Every call returns a response with `Success`. Check it before using the content, and log the failure. Don't assume a call worked.
- An administrator can put the package in a **Zone**, which limits what it can reach through `Client`. A denied call returns 403 "Zone Access Denied". List what the package calls in its `Readme.md` so administrators can allow it.

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
- **Readme:** a `Readme.md` (set to copy to the output directory) is shown with the package on the Instance. Describe what the package does, its configuration parameters, and what it calls through `Client`.
- **Source control:** `bin`, `obj`, the debug `volume` folder and `.thp` files stay out of it (see `.gitignore`).

## Troubleshooting

| Problem | Cause |
| --- | --- |
| Doesn't load on the Instance ("Could not load file or assembly 'TrakHound.Common'") | Built against a newer TrakHound than the Instance |
| "Assembly Not Found" or "No ... Types Found" (Explorer > Diagnostics > Modules) | The assembly isn't named after the package ID, or has no public, non-abstract class of the right base type |
| `Client` calls fail in the debug host ("Instance Not Reachable") | The debug host connects to `instanceUrl` in `trakhound.debug.json` (default `http://localhost:8472`); start that Instance, or set `apiKey` if it requires one |
| `Client` calls return 403 "Zone Access Denied" | The package's Zone doesn't allow the call |
