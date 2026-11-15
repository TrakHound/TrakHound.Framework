# TrakHound.Host.Template
Create a standalone host for a TrakHound Api, Worker or Function package. The host runs the package without an Instance: as a console app, a Windows Service, a Linux (systemd) service, an IIS application, a container or a cloud web app. The package project doesn't change, so the same code still installs on an Instance.

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
`th host create` in the package project's folder creates it for you, with the module class, the debug settings and (with `--api-key`) an API key filled in (see the [CLI README](https://github.com/TrakHound/TrakHound.Framework/tree/main/cli#host-commands)). Or create the host in the folder next to the package project (the host references `../[PACKAGE_ID]/[PACKAGE_ID].csproj`):
```
dotnet new trakhound.host -n [PACKAGE_ID].Host --module [PACKAGE_ID] --kind api
```

##### Parameters
| Name | Default Value | Description |
| --- | --- | --- |
| `--kind` | `api` | The type of the package: `api` (a web app), `worker` (runs until the host stops) or `function` (runs once and exits, ex. a scheduled job) |
| `--module` | | The name of the package project (its folder must be next to the host's folder) |
| `--trakhoundVersion` | The TrakHound version the templates were released with | Version of the TrakHound NuGet packages to reference. Use the same version as the package project |
| `--instanceUrl` | `http://localhost:8472` | The Instance the host connects to in the Development environment (**appsettings.Development.json**) |

## Project Structure
| File | Description |
| --- | --- |
| `Program.cs` | Runs the package. An Api is protected with API keys and serves `/openapi.json` and `/health`. A Worker starts and stops with the host. A Function runs once and writes its response to the console (the exit code is 0 when it succeeds). Uses the `Controller`, `Worker` or `Function` class of the package: change it if yours is named differently |
| `appsettings.json` | The `"TrakHound"` settings: the Instance, the configuration Parameters, the API keys callers send (Api) and the input (Function) |
| `appsettings.Development.json` | Connects to `--instanceUrl` when run in the Development environment |
| `Properties/launchSettings.json` | Api only: runs the host at `http://localhost:5093` and opens the OpenAPI document |
| `Dockerfile` | Builds a container image. Build it from the parent folder: `docker build -f [PACKAGE_ID].Host/Dockerfile .` |
| `.gitignore` | Leaves build output and the `volume` folder out of source control |

## Settings
Settings come from **appsettings.json**, environment variables (ex. `TrakHound__InstanceUrl`) or the command line (ex. `--TrakHound:Parameters:interval=1000`). See section 2.10 of the [TrakHound Developer Guide](https://github.com/TrakHound/TrakHound.Framework/blob/main/docs/developer-guide.md) for each setting.

An Api host refuses every request until it has an API key. Add the SHA-256 hash (hex) of a key to `"ApiKeys"`, and callers send the key in the `X-API-Key` header:

```json
"ApiKeys": [ { "Name": "line-1", "Hash": "..." } ]
```

#### Windows
```
$key = "..."; (Get-FileHash -Algorithm SHA256 -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($key)))).Hash.ToLower()
```

#### Linux
```
printf %s "$KEY" | sha256sum
```

## Run
```
dotnet run
```

#### Windows Service
```
dotnet publish -c Release -o C:\Services\MyHost
sc.exe create MyHost binPath= "C:\Services\MyHost\[PACKAGE_ID].Host.exe"
```

#### Linux (systemd)
Publish the host and point a unit's `ExecStart` at it, with `Type=notify`.

#### IIS
Publish the host to a folder and add it as an IIS application (the publish output includes `web.config`). The ASP.NET Core Hosting Bundle must be installed.
