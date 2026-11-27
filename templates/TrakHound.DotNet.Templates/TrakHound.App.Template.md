# TrakHound.App.Template
Create a TrakHound App package. App packages are Blazor components that provide a browser-based user interface hosted by a TrakHound Instance.

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
dotnet new trakhound.app -n [PACKAGE_ID]
```
The project name is used as the package `id` in **trakhound.package.json**.

##### Parameters
| Name | Default Value | Description |
| --- | --- | --- |
| `--trakhoundVersion` | The TrakHound version the templates were released with | Version of the TrakHound NuGet packages to reference. Set it to the TrakHound version of the Instance you deploy to (or older) |
| `--instanceUrl` | `http://localhost:8472` | The Instance the debug host in **Program.cs** connects to |
| `--publisher` | | Sets the `publisher` metadata in **trakhound.package.json** |
| `--description` | | Sets the `description` metadata in **trakhound.package.json** |
| `--packageServer` | | Sets the Package Server of the `default` Publish Target in **trakhound.package.publish.json**, which is then its `defaultTarget` |
| `--organization` | | Sets the Organization of the `default` Publish Target in **trakhound.package.publish.json**, which is then its `defaultTarget` |
| `--localTarget` | `true` | Adds the `local` Publish Target to **trakhound.package.publish.json** : the Dev Package Server of the development Instance at `instanceUrl` (ex. a `trakhound/instance-dev` container), with a prerelease version so it can be published again with uncommitted changes. Set it to `false` in a repository that has a shared **trakhound.package.publish.json** (without `--packageServer` or `--organization`, the file is then left out) |

The package `.defaultRoute` is generated from the project name (lowercase, with `.` replaced by `/`). For example, `MyCompany.Sensors` becomes `mycompany/sensors`.

## Project Structure
| File | Description |
| --- | --- |
| `Pages/Index.razor` | Default page of the App |
| `Layout.razor` | App layout |
| `Debug.razor` / `DebugRoutes.razor` | Debug host page and router (used only when running locally) |
| `Program.cs` | Debug host startup |
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

Open the App in a web browser: http://localhost:5000

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
> **trakhound.package.publish.json** has the `local` Publish Target (unless `--localTarget false`), so a new project publishes to the Dev Package Server of a `trakhound/instance-dev` container (`docker run -d -p 127.0.0.1:8472:8472 trakhound/instance-dev`) without any setup. The CLI only uses the nearest **trakhound.package.publish.json**, so in a repository that has a shared one in a parent directory, delete the project's file (or create the project with `--localTarget false`) to use it.
