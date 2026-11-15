# TrakHound.Worker.Template
Create a TrakHound Worker package. Workers are long-running processes that are typically used to process, import, or export data that requires a constant connection or subscription.

| Type | Name | Downloads | Link |
| --- | --- | --- | --- |
| NuGet | TrakHound.DotNet.Templates | <img src="https://img.shields.io/nuget/dt/TrakHound.DotNet.Templates?style=for-the-badge&logo=nuget&label=%20&color=%23333"/> | [nuget.org/packages/TrakHound.DotNet.Templates](https://www.nuget.org/packages/TrakHound.DotNet.Templates) |

## Get Started

#### Install Template
Use the **[dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/)** to install the templates:
```
dotnet new install TrakHound.DotNet.Templates
```

#### Create New Project
Create a new project from the template in the current working directory:
```
dotnet new trakhound.worker -n [PACKAGE_ID]
```
The project name is used as the package `id` in **trakhound.package.json**.

##### Parameters
| Name | Default Value | Description |
| --- | --- | --- |
| `--trakhoundVersion` | The TrakHound version the templates were released with | Version of the TrakHound NuGet packages to reference. Set it to the TrakHound version of the Instance you deploy to (or older) |
| `--instanceUrl` | `http://localhost:8472` | The Instance the debug host in **Program.cs** connects to |
| `--publisher` | | Sets the `publisher` metadata in **trakhound.package.json** |
| `--description` | | Sets the `description` metadata in **trakhound.package.json** |
| `--managementServer` | | Sets the Package Server of the `default` Publish Target in **trakhound.package.publish.json** (the file is only created if this or `--organization` is set) |
| `--organization` | | Sets the Organization of the `default` Publish Target in **trakhound.package.publish.json** |

## Project Structure
| File | Description |
| --- | --- |
| `Worker.cs` | Worker implementation. Starts a loop in `OnStartAsync` that runs every `interval` milliseconds (a configuration parameter, default 5000) and ends it in `OnStopAsync` |
| `Program.cs` | Debug host that starts the Worker and stops it on Ctrl+C |
| `trakhound.package.json` | Package manifest |
| `trakhound.package.publish.json` | Publish Targets (where to publish) |
| `.gitignore` | Leaves build output, the debug `volume` folder and `.thp` files out of source control |
| `AGENTS.md` / `CLAUDE.md` | Instructions for AI coding tools (ex. Claude Code): the commands, conventions and rules for this package type. `CLAUDE.md` imports `AGENTS.md` |

## Build & Develop
Use the **[dotnet CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/)** to build the project:
```
dotnet build -c:Debug
```

#### Run
The project runs as a standalone debug host that connects to the TrakHound Instance set by `--instanceUrl` (default `http://localhost:8472`, edit **Program.cs** to change it):
```
dotnet run -c:Debug
```

## Publish Package
Packages are published using the **[TrakHound CLI](https://github.com/TrakHound/TrakHound.Framework/tree/main/cli)** (`th`). The template includes **publish-package.bat** and **publish-package.sh** scripts that run:
```
th packages dotnet --project-configuration Release --publish
```

#### Windows
```
publish-package.bat
```

#### Linux
```
./publish-package.sh
```

The version is the `version` prefix in **trakhound.package.json** (ex. `0.1`) plus the git height, so `0.1` four commits later publishes `0.1.4`. Change the prefix to start a new version line.

> [!NOTE]
> A Publish Target must be set in **trakhound.package.publish.json** before publishing : in the project (created when **managementServer** or **organization** is passed when creating it), or once in a parent directory for every project below it.
