# TrakHound CLI
Command Line Interface application to perform tasks within the TrakHound Framework.

## Get Started

### Windows Installation

#### Install DotNet CLI
The TrakHound CLI application requires the [DotNet CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/) to build the package project files and must be installed.

#### Install TrakHound-CLI
Download and run the installer from the link below:

<table>
    <thead>
        <tr>
            <th style="text-align: left;min-width: 100px;">Name</th>
            <th style="text-align: left;">Platform</th>
            <th style="text-align: left;min-width: 100px;">Type</th>
            <th style="text-align: left;">Link</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td>TrakHound-CLI</td>
            <td>Windows</td>
            <td>Installer</td>
            <td><a href="https://github.com/TrakHound/TrakHound.Framework/releases/latest">https://github.com/TrakHound/TrakHound.Framework/releases/latest</a></td>
        </tr>  
    </tbody>
</table>

### Linux Installation

#### Install DotNet CLI (Snap)
The TrakHound CLI application requires the [DotNet CLI](https://learn.microsoft.com/en-us/dotnet/core/install/linux-snap-sdk) to build the package project files and must be installed using Snap using the command below:
```
sudo snap install dotnet-sdk --classic
```

#### Install trakhound-cli (Snap)
Install the latest revision of the **[trakhound-cli](https://snapcraft.io/trakhound-cli)** Snap package using the command below:
```
sudo snap install trakhound-cli --devmode
```
> Note: Install using **devmode** as the trakhound-cli application requires access to the /usr/lib/dotnet-sdk/current directory. This may be changed in a later version to remove the devmode requirement.

Set the 'th' alias to point to trakhound-cli using the command below:
```
sudo snap alias trakhound-cli th
```

## Usage
```
th [group] [command] [parameters] [options]
```
- Run `th` or `th help` to list all commands, and `th help [group] [command]` (or `th [group] [command] --help`) for details on a command
- Options can be specified in any order using `--option value` or `--option=value` (ex. `--include-debug-symbols false`). Some options have short aliases (ex. `-v` for `--version`)
- Relative paths are resolved against the current working directory
- The exit code is `0` on success, `1` if the command failed and `2` if the command line was invalid

## Configuration
Settings are read from the files below (later sources override earlier ones):
1. `cli.config.yaml` in the application directory
2. `~/.trakhound/cli.config.yaml` in the user directory
3. Environment Variables

| Setting | Environment Variable | Description |
|---|---|---|
| `managementServer` | `TRAKHOUND_MANAGEMENT_SERVER` | The default Package Server URL (`https://packages.trakhound.com` if not set) |
| `organization` | `TRAKHOUND_ORGANIZATION` | The default Organization |
| `apiToken` | `TRAKHOUND_API_TOKEN` | The API Token sent to the Package Server |
| `dotnetSdkLocation` | `TRAKHOUND_DOTNET_SDK` | The directory containing the `dotnet` executable (if not on the PATH) |

## Packages Commands

| Command | Description |
|---|---|
| `th packages list [path] [--category]` | List the installed packages |
| `th packages install [packageId] [--version] [--path] [--server] [--organization]` | Download and install a package (and its dependencies). If no Package ID is specified, the packages in `trakhound-packages.json` are installed |
| `th packages install --file <path.thp> [--path] [--no-dependencies]` | Install a local package file. Its dependencies are downloaded from the Package Server unless `--no-dependencies` is set. A package file path can also be passed in place of the Package ID |
| `th packages install ... --configure [--profile] [--group]` | Also add a configuration for the package and each dependency that doesn't have one yet, the same one installing from Packages in the Admin interface adds (its `.defaultName`, `.defaultRoute` and `.defaultVolumeId`). An existing configuration without a Volume ID that runs the installed version gets the `.defaultVolumeId`. Configurations go in `<path>/config/<profile>` (profile `default` unless set). `--group` puts the added Apis, Apps, Functions and Workers in that Group, and creates the Group if it doesn't exist |
| `th packages uninstall <packageId> [--version] [--path]` | Uninstall a package |
| `th packages verify [packageId] [--file] [--version] [--path] [--profile] [--require-signature]` | Check that a package's files match its manifest (`package.manifest.json`), and who signed it. Checks a `.thp` file, or the installed packages (all of them if no Package ID is specified). Lists changed, missing and added files, and exits with an error if a package fails or has no manifest. `--require-signature` also fails a package that isn't signed by a trusted publisher |
| `th packages rebuild <path.thp> [--repository] [--project] [--allow-untrusted] [--path] [--profile]` | Clone the commit recorded in a package, build it again and compare the files, to check that the package was built from that source. Refuses a package without a trusted signature unless `--allow-untrusted` is set, because building runs the repository's code |
| `th packages keygen [name] [--output]` | Create a key pair for signing packages (`[name].private.pem` and `[name].public.pem`) |
| `th licenses keygen [name] [--output]` | Create a key pair for issuing licenses (`[name].license.private.pem` and `[name].license.public.pem`) |
| `th licenses sign --product <id> --device <id> [--limit] --expires <yyyy-MM-dd>\|--days <n>\|--permanent [--key]` | Issue a License Code. `--any-device` replaces `--device` for a license that works on any device. The key can also come from `TRAKHOUND_LICENSE_KEY` |
| `th licenses inspect <code> [--public-key] [--product] [--device]` | Show what a License Code contains and check its signature, product and device |
| `th licenses device-id` | Show this machine's Device ID |
| `th trust list\|add\|remove [--path] [--profile]` | Manage the publishers a configuration profile trusts. `th trust add <public-key-file> --name <name> [--prefix <id-start>]` limits a publisher to the package IDs that start with a prefix |
| `th packages pack [path] [--output] [--version] [--version-suffix] [--sign-key] [--allow-dirty] ...` | Create a package file (`.thp`) from a directory |
| `th packages publish [path] [--version] [--version-suffix] [--server] [--organization] [--sign-key] [--allow-dirty] [--target] [--skip-existing] ...` | Create a package from a directory and upload it |
| `th packages push <path> [--server] [--organization]` | Upload an existing package file (`.thp`) |
| `th packages dotnet [path] [--output] [--publish] [--version] [--version-suffix] [--include-debug-symbols] [--include-source] [--sign-key] [--allow-dirty] [--target] [--skip-existing]` | Build a DotNet project and create a package from it |
| `th packages build [path] [--only] [--project] [--fail-fast] [--dry-run] [--version-suffix] [--sign-key] [--allow-dirty] [--target] [--skip-existing]` | Build (and optionally publish) every project listed in a `trakhound.build.json` file |

### Install Directory
The `path` used by `list`, `install` and `uninstall` is the root directory (ex. a TrakHound Instance or Deployment directory). Packages are installed in its `packages` subdirectory (`[path]/packages/[category]/[id]/[version]`) and the installed packages are recorded in `[path]/packages/trakhound-packages.json`. A `trakhound-packages.json` file in the root directory (the legacy location) is still read by `install` when no Package ID is specified.

### Versions
Package versions come from git, the same way the TrakHound Framework's own versions do. The `version` in `trakhound.package.json` is a two part prefix that you set by hand:
```json
{ "id": "Contoso.Orders.Api", "category": "api", "version": "0.3" }
```
- The package's version is `[major].[minor].[height]` (ex. `0.3.4`). The **height** is the number of commits in the repository, including the commit itself, since the `version` line in that `trakhound.package.json` last changed. It counts every commit in the repository, not only the ones in the package directory, since code outside it (ex. a shared library) can change the package too.
- Change the prefix to start a new version line (ex. `"0.4"`). Until that change is committed the height is `0` (`0.4.0`).
- The same commit always gets the same version, on any machine. `pack`, `publish`, `dotnet` and `build` all use it, nothing is read from a Package Server to choose it, and the CLI never writes a version to any file.
- A Publish Target's `versionSuffix` (below) or `--version-suffix` adds a prerelease label (ex. `0.3.4-dev.20261114153000`). `--version-suffix` replaces the Target's.
- `--version` replaces the prefix and height (ex. `--version 1.0.0`).
- The height needs the full history, so the CLI stops in a shallow clone (`git fetch --unshallow`). Outside a git repository the height is `0`, with a warning.
- Before uploading, the CLI reads the versions on each destination and stops if the version is already published, since a version is never replaced. Commit a change (which adds to the height) or change the prefix.
- `--skip-existing` skips a destination that already has the version, instead of stopping, when that package was built from the same commit without uncommitted changes (its `repositoryCommit` and `repositoryDirty`). It is the same source, so there is nothing new to upload (ex. a package published on its own, or a `build` that stopped partway). The working tree must be a clean commit, and a version published from another commit is still an error. `build` reports the project as `ALREADY PUBLISHED`, and the packages that depend on it are still published.

### Publish Targets
Where packages are published is set in a `trakhound.package.publish.json` file, as one or more named **Publish Targets**:
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
```
th packages publish                    # the defaultTarget (development) : 0.3.4-dev.20261114153000
th packages publish --target release   # 0.3.4
```
- The nearest `trakhound.package.publish.json` is used, starting in the package directory and continuing up through its parent directories, so one file at the root of a repository covers every package below it. The nearest file is used as a whole (files aren't merged). Comments and trailing commas are allowed.
- `--target` (`-t`) picks the Target on `publish`, `dotnet --publish` and `build`. Without it the file's `defaultTarget` is used, and the CLI stops if there's none.
- The file holds only `defaultTarget` and `targets`. A `version` or a top-level `destinations` (the old format), or a misspelled setting, is an error, so it isn't silently ignored.
- `--server` and `--organization` (or `server` and `organization` in a build file) are only for a package without a `trakhound.package.publish.json`. With one, they're an error.
- A package is published to every destination of the Target or to none: if any destination's versions can't be read (ex. a server on a network you're not connected to), nothing is uploaded.

| Target setting | Description |
|---|---|
| `destinations` | The Package Servers to publish to : `managementServer`, `organization`, and optionally `apiTokenVariable` |
| `versionSuffix` | A prerelease label added to every version published to the Target. `{timestamp}` is the UTC time of the run (`yyyyMMddHHmmss`, the same for every package in one `build`). `{branch}` is empty on `main` (or `master`, or a detached HEAD) and `-[branch]` on any other branch, with characters other than letters, digits and `-` replaced by `-` (`dev{branch}.{timestamp}` is `dev.20261114153000` on `main` and `dev-feature-opc-ua.20261114153000` on `feature/opc-ua`). Without it, the Target publishes stable versions |
| `allowDirty` | Packages can be built (and signed) from a working tree with uncommitted changes. Requires a `versionSuffix` with `{timestamp}`, so every build has its own version |
| `requireSigning` | Packages must be signed (`--sign-key` or `TRAKHOUND_SIGNING_KEY`) and built from a commit : uncommitted changes are refused, even with `--allow-dirty` |

A Target without `allowDirty` publishes the version of the commit, so it refuses uncommitted changes before building. `--allow-dirty` overrides that for a Target without `requireSigning`.

**Api Tokens.** Each destination's `apiTokenVariable` names the environment variable that holds its Api Token (sent in the `X-TrakHound-Api` header), so one repository can publish to servers that need different keys. A destination without one uses the CLI's `apiToken` (`TRAKHOUND_API_TOKEN`). If a named variable isn't set, the CLI stops before building anything. The tokens themselves never go in the file.

**Prereleases on Instances.** Instances only select a prerelease when a dependency asks for one (ex. `"*-*"`) or no stable version matches. A server that only has development builds works without that, but an Instance that uses a development and a release server prefers the release's stable versions.

### Build Files
A `trakhound.build.json` file lists multiple DotNet projects so they can be built (and published) with a single command. Projects are built one at a time using the same settings as `th packages dotnet`, and nothing is published until every project has built. `defaults` apply to every project unless the project overrides them, and relative paths are resolved against the build file directory.
```json
{
  "defaults": {
    "projectConfiguration": "Release",
    "includeDebugSymbols": false,
    "publish": true
  },
  "projects": [
    { "path": "modules/TrakHound.Http.Service" },
    { "path": "modules/TrakHound.Mqtt.Service", "includeSource": true },
    { "path": "modules/TrakHound.Packages.App", "publish": false, "output": "./dist" }
  ]
}
```
Project settings: `path` (required), `output`, `projectConfiguration`, `includeDebugSymbols`, `includeSource`, `publish`, `version`, `server` and `organization`.

```
th packages build
th packages build ./trakhound.build.json --only Http,Mqtt
th packages build --dry-run
```
- `--only` builds the projects whose path contains one of the comma separated values
- `--project` builds the project with that directory name or path. It must match a whole project (`--project TrakHound.Packages.Api` doesn't also select `TrakHound.Packages.Api.Client`) and can be repeated
- `--fail-fast` stops at the first project that fails
- `--dry-run` lists the projects (in the order they are built) and their resolved settings without building
- A project is built and published after the projects in the build file that its package depends on (the `dependencies` in its `trakhound.package.json`). Otherwise the listed order is kept. Dependencies on packages that aren't in the build file are ignored.
- If a project fails to build, none of the packages are published (the summary lists the built ones as `NOT PUBLISHED`). Package files are still written for projects with an `output`.
- If a package fails to upload, the packages that depend on it aren't published. The others still are.
- A summary is printed at the end and the exit code is `1` if any project failed

### Example
Install the package **TrakHound.Entities.Api** using the command below:
```
th packages install TrakHound.Entities.Api
```

Build, package and publish a DotNet project without debug symbols:
```
th packages dotnet ./MyFunction --publish --include-debug-symbols false
```

## Deployments Commands

| Command | Description |
|---|---|
| `th deployments create <profileId> [path] [--version] [--output]` | Create a deployment file (`.thd`) from a directory |
| `th deployments install <target> [path]` | Install a deployment from a local file |
| `th deployments clean [path] [--yes]` | Remove the installed packages, configurations, packages file and deployment file (prompts for confirmation unless `--yes` is set) |

## Config Commands
The `config` commands add, change, remove and check the configuration files and variables of a Configuration Profile, so an Instance can be set up from a script. They read and save the files the same way the Instance does.

| Command | Description |
|---|---|
| `th config list [path] [--profile] [--category]` | List the configurations |
| `th config get <category> <id> [path] [--profile]` | Print the YAML of a configuration |
| `th config add <category> <file> [path] [--profile] [--id]` | Add a configuration from a YAML file, or replace the configuration with the same ID. The ID comes from `--id`, then the `id` in the file, then the filename |
| `th config set <category> <id> [path] [--profile] [--param] [--remove-param] [--property]` | Change the parameters or properties of a configuration. It's created if it doesn't exist |
| `th config remove <category> <id> [path] [--profile]` | Remove a configuration |
| `th config validate [path] [--profile] [--package-path]` | Check the profile for problems that stop configurations from loading. Exits with `1` if it finds errors |
| `th config variables [path] [--profile]` | List the variables |
| `th config set-variable <name> <value> [path] [--profile]` | Add or change a variable |
| `th config remove-variable <name> [path] [--profile]` | Remove a variable |

- `path` is the root directory and `--profile` is the Configuration Profile (`default` if not specified). The files are in `[path]/config/[profile]`. The root directory is the Instance directory, except for a Windows Service (`C:\ProgramData\TrakHound\Instance`, run as an Administrator) and Linux (`/var/lib/trakhound/instance`).
- The categories are `api`, `app`, `function`, `group`, `identity`, `router`, `service`, `worker` and `zone`.
- `--param name=value`, `--remove-param name` and `--property name=value` can be repeated. Parameters are saved as text, the same as parameters read from a file. Properties are the configuration's other settings (ex. `packageId`, `routerId`). Separate list values with commas (ex. `--property mcpInclude=a,b`). Settings that aren't text, numbers, true/false or lists (ex. a router's routes) can only be set with `add`.
- Set `packageVersion` as well as `packageId`: the Instance doesn't load a configuration without a version. Use `*` for the latest installed version.
- Secret parameters and variables (ex. `password`, `apiKey`) are encrypted when they're saved, with the key in `[path]/config/.secretkey`. The key is created if it doesn't exist. An Instance can only read secrets encrypted with its own key, so run the commands against the Instance's own directory. A value typed on the command line is kept in the shell history, so for a secret, consider a variable that reads an environment variable of the Instance (ex. `th config set-variable dbPassword '${env:DB_PASSWORD}'`).
- A running Instance applies configuration changes within a few seconds, without a restart. Variables are read when the Instance starts, so restart it after changing them.
- On Linux, run the commands as the service user (ex. `sudo -u trakhound th config list /var/lib/trakhound/instance`) so the Instance can read the files they create.

### Validate
`th config validate` reports:
- Files that can't be read, files without an `id`, and IDs used by more than one file.
- Configurations whose package isn't installed, has no installed version that matches `packageVersion`, or has no `packageVersion`. Packages are read from `[path]/packages`, or `--package-path` (ex. `C:\ProgramData\TrakHound\Instance\packages` for a Windows Service).
- A `routerId` that isn't the ID or name of a router, or the ID of a service.
- Parameters that reference a variable (`${name}`) that isn't defined, and secrets that can't be decrypted with the profile's key.
- Invalid ports in `instance.config.yaml`.
- Warnings for a `groupId` that isn't a configured Group, and for zone members (Modules or `group:<id>`) that aren't configured.

### Example
```
th config set worker my-worker ./instance --property packageId=MyCompany.MyWorker --property packageVersion='*' --param interval=5000
th config set-variable dbHost sql01 ./instance
th config add api ./my-api.yaml ./instance --profile staging
th config validate ./instance
th config remove worker my-worker ./instance
```

## Dev Commands
The `dev` commands run a package project inside a local Instance while you work on it. `th dev run` builds the project (Debug, with `.pdb` files), installs it in the Instance's `packages` folder, adds a configuration for it (`[path]/config/[profile]/[category]/dev-[packageId].yaml`), and reinstalls it each time a project file changes. A running Instance picks up each change within a few seconds.

| Command | Description |
|---|---|
| `th dev run [path] [--instance] [--profile] [--route] [--project-configuration] [--no-watch] [--keep]` | Build, install and configure the project, then reinstall it on each change. Ctrl+C stops and removes the configuration and the dev versions (unless `--keep` is set) |
| `th dev clean [path] [--instance] [--profile]` | Remove the configuration and the dev versions that `th dev run` installed for the project |

- `path` is the project directory (or `.csproj` file), default the current directory. `--instance` is the Instance's root directory (with its `packages` and `config` directories); if it's not set, the `TRAKHOUND_DEV_INSTANCE` environment variable is used.
- Each build is installed as a new version: the next 4th number of the package version (ex. `0.0.1.1`, `0.0.1.2`), since the Instance only loads new code for a new version. The configuration is set to that exact version, and the previous dev version is uninstalled once the Instance has switched to it. The versions installed are kept in the project's `obj/trakhound-dev.json`.
- The configuration's parameters are the `parameters` in the project's `trakhound.debug.json`. An Api or App gets the route in `--route`, the package's `.defaultRoute` metadata, or the package ID. A Function's `functionId` is the package ID.
- The Instance's process ID (from its Boot History, `boots.json`) is printed, to attach a debugger to.

### Example
```
th dev run ./Contoso.Hello --instance ./instance
th dev clean ./Contoso.Hello --instance ./instance
```

## Host Commands
The `host` commands create a standalone host for a package project: a project next to it that runs the package without an Instance, as a console app, a Windows Service, a systemd service, an IIS application or a container (see section 2.10 of the [Developer Guide](../docs/developer-guide.md)). The package project doesn't change.

| Command | Description |
|---|---|
| `th host create [path] [--name] [--class-name] [--api-key]` | Create the host project for an Api, Worker or Function package from the `trakhound.host` template |

- `path` is the project directory (or `.csproj` file), default the current directory. The host is created next to it, named after the project followed by `.Host` (or `--name`).
- The kind of host comes from the package's category. The module class (the Api controller, the Worker or the Function) is found in the project's source with its namespace; set `--class-name` when there is more than one Worker or Function.
- The settings in the project's `trakhound.debug.json` become the host's development settings (`appsettings.Development.json`).
- `--api-key` generates an API key for an Api host, adds its hash to `appsettings.json` and prints the key. An Api host refuses every request until it has one.
- The host references the TrakHound NuGet packages at the version the project uses. A project in this repository, which references the libraries as projects, gets a host that does too.
- The `trakhound.host` template must be installed (`dotnet new install TrakHound.DotNet.Templates`).

### Example
```
th host create ./Contoso.Hello --api-key
dotnet run --project ./Contoso.Hello.Host
```

## Instance Commands
The `instance` commands read and change the Instance's settings (`[path]/config/[profile]/instance.config.yaml`), such as its ports, trusted proxies and which interfaces it serves.

| Command | Description |
|---|---|
| `th instance settings [path] [--profile]` | Print the settings |
| `th instance get <name> [path] [--profile]` | Print the value of a setting |
| `th instance set <name> <value> [path] [--profile]` | Change a setting |

- Names are the keys in `instance.config.yaml` (ex. `httpPort`, `mcpEnabled`). Use `.` for nested settings (ex. `metrics.enabled`, `rateLimits.api.permitLimit`). Separate list values with commas (ex. `th instance set trustedProxies 127.0.0.1,10.0.0.0/8`).
- If the file doesn't exist yet, `set` creates it with the defaults the Instance would use, and `get` and `settings` show those defaults.
- A setting can reference a Variable (ex. `httpPort: ${port}`). `get` prints the reference, `set` replaces it with the value, and the other settings' references are kept. `th config validate` resolves them with the profile's Variables and warns about the ones it can't.
- The Instance reads the file when it starts, so restart it to apply a change.
- `path` and `--profile` work the same as for the `config` commands.

### Example
```
th instance set httpPort 8080 ./instance
th instance set adminHttpPort 8473 ./instance
th instance get httpPort ./instance
```
