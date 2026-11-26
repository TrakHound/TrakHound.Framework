# TrakHound.DotNet.Templates
`dotnet new` project templates for creating TrakHound Api, App, Function, Worker, Service, and Identity packages, and standalone hosts that run a package without an Instance.

For a walkthrough from creating a package to running it on an Instance, see the [TrakHound Developer Guide](https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md).

## Downloads
| Type | Name | Downloads | Link |
| --- | --- | --- | --- |
| NuGet | TrakHound.DotNet.Templates | <img src="https://img.shields.io/nuget/dt/TrakHound.DotNet.Templates?style=for-the-badge&logo=nuget&label=%20&color=%23333"/> | [nuget.org/packages/TrakHound.DotNet.Templates](https://www.nuget.org/packages/TrakHound.DotNet.Templates) |

## Install
Use the **[dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/)** to install the templates:
```
dotnet new install TrakHound.DotNet.Templates
```

## Templates
| Name | Short Name | Package Category | Command | Docs |
| --- | --- | --- | --- | --- |
| TrakHound.Api.Template | `trakhound.api` | api | `dotnet new trakhound.api` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Api.Template.md) |
| TrakHound.App.Template | `trakhound.app` | app | `dotnet new trakhound.app` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.App.Template.md) |
| TrakHound.Function.Template | `trakhound.function` | function | `dotnet new trakhound.function` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Function.Template.md) |
| TrakHound.Worker.Template | `trakhound.worker` | worker | `dotnet new trakhound.worker` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Worker.Template.md) |
| TrakHound.Service.Template | `trakhound.service` | service | `dotnet new trakhound.service` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Service.Template.md) |
| TrakHound.Identity.Template | `trakhound.identity` | identity | `dotnet new trakhound.identity` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Identity.Template.md) |
| TrakHound.Host.Template | `trakhound.host` | | `dotnet new trakhound.host` | [Learn More](https://github.com/TrakHound/TrakHound.Framework/blob/main/templates/TrakHound.DotNet.Templates/TrakHound.Host.Template.md) |

## Parameters
The package templates accept the following parameters (`--instanceUrl` applies to every template except Service). The host template has its own (see its page):

| Name | Default Value | Description |
| --- | --- | --- |
| `--trakhoundVersion` | The TrakHound version the templates were released with | Version of the TrakHound NuGet packages to reference. Set it to the TrakHound version of the Instance you deploy to (or older) |
| `--instanceUrl` | `http://localhost:8472` | The Instance the debug host in **Program.cs** connects to |
| `--publisher` | | Sets the `publisher` metadata in **trakhound.package.json** |
| `--description` | | Sets the `description` metadata in **trakhound.package.json** |
| `--managementServer` | | Sets the Package Server of the `default` Publish Target in **trakhound.package.publish.json**, which is then its `defaultTarget` |
| `--organization` | | Sets the Organization of the `default` Publish Target in **trakhound.package.publish.json**, which is then its `defaultTarget` |
| `--localTarget` | `true` | Adds the `local` Publish Target to **trakhound.package.publish.json** : the Dev Package Server of the development Instance at `instanceUrl` (ex. a `trakhound/instance-dev` container), with a prerelease version so it can be published again with uncommitted changes. Set it to `false` in a repository that has a shared **trakhound.package.publish.json** (without `--managementServer` or `--organization`, the file is then left out) |

#### Example
```
dotnet new trakhound.api -n MyCompany.MyApi --publisher "MyCompany" --managementServer "http://localhost:8472" --organization "Public"
```

## Project Files
Each generated project contains:

| File | Description |
| --- | --- |
| `trakhound.package.json` | Package manifest (id, category, and metadata) |
| `trakhound.package.publish.json` | The Publish Targets (where to publish). Can instead be set once for several projects in a parent directory (see the [CLI README](https://github.com/TrakHound/TrakHound.Framework/tree/main/cli#publish-targets)). The version is the `version` prefix in `trakhound.package.json` plus the git height |
| `README.md` | What the package does and how to configure it. It's copied to the package as `Readme.md` and shown with the package on the Instance and on the package server (ex. packages.trakhound.com), so fill in its placeholder text and use absolute links |
| `Program.cs` | Debug host that runs the package without an Instance (left out of the package) |
| `publish-package.bat` / `publish-package.sh` | Packs and publishes the package using the [TrakHound CLI](https://github.com/TrakHound/TrakHound.Framework/tree/main/cli) |
| `.gitignore` | Leaves build output, the debug `volume` folder and `.thp` files out of source control |
| `AGENTS.md` / `CLAUDE.md` | Instructions for AI coding tools (ex. Claude Code): the commands, conventions and rules for this package type. `CLAUDE.md` imports `AGENTS.md` |
