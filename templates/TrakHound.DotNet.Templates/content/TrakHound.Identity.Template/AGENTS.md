# UserPackageId

This project is an **Identity** provider package: signs people in for a TrakHound Instance's security profiles. Its package ID is `UserPackageId` (category `identity`).

Developer guide: https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md

## Commands

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Authenticate a request with a token (sent in X-Example-Token) | `dotnet run -- <token>` |
| Authenticate with the sample token (the first launch profile) | `dotnet run` |
| Sign in with a username and password | `dotnet run -- <username> <password>` |
| Build the package file (`.thp`) | `th packages dotnet --output ./out` |
| Publish to the Publish Target in the nearest `trakhound.package.publish.json` | `th packages dotnet --publish [--target <name>]` (or `publish-package.bat` / `.sh`) |

`th` is the TrakHound CLI. The version is the `version` prefix in `trakhound.package.json` plus the git height (the commits since the prefix changed), so commit before publishing. Change the prefix to start a new version line.

## Files

| File | Purpose |
| --- | --- |
| `Module.cs` | The provider (a `TrakHoundIdentityProviderBase`) |
| `Program.cs` | Local debug host that runs the provider without an Instance (left out of the package) |
| `trakhound.debug.json` | The debug host's settings: `instanceUrl`, `apiKey` and the configuration `parameters` (left out of the package). Keep real credentials out of source control |
| `Properties/launchSettings.json` | Launch profiles for `dotnet run`, Visual Studio and VS Code (with and without a token) |
| `.vscode/` | VS Code: the launch configurations (the debug host, and Attach to Instance for `th dev run`), tasks (build, Build package, Publish package, Dev run, Dev clean), the recommended extension (C# Dev Kit) and the Instance's MCP Server (`mcp.json`) |
| `trakhound.package.json` | The package manifest: `id`, `category`, `metadata` (publisher, description, ...) and `dependencies` |
| `trakhound.package.publish.json` | Where to publish : the Publish Targets (only created when a package server was given; otherwise the nearest one in a parent folder is used) |
| `README.md` | What the package does and how to configure it, shown with the package on the Instance and the package server |
| `UserPackageId.csproj` | The project. The assembly must stay named `UserPackageId.dll` (the package ID) |

## Writing the provider

- Derive from `TrakHoundIdentityProviderBase` (namespace `TrakHound.Security`).
- `Features` declares how sign-in works: `Token` (a header or query value on each request, `OnAuthenticate`), `Redirect` (an external sign-in page, `OnChallenge` and `OnHandleCallback`) and/or `Credentials` (a username and password, `OnSignIn`). Implement `OnSignOut` if needed.
- Return `TrakHoundIdentityResult.Success(identity)` (with the user's ID, name and roles), `NoResult()` when the request isn't for this provider, `Fail(message)` for rejected credentials, or `Error(message)`.
- Never log tokens, passwords or other secrets.
- Test real sign-ins on an Instance with Security > Providers > **Test Sign In**.
- Reference implementations in the TrakHound.Framework repository: `modules/TrakHound.Token.Identity`, `TrakHound.Ldap.Identity` and `TrakHound.Oidc.Identity`.

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
