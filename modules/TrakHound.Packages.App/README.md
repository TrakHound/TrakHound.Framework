# TrakHound.Packages.App

An App for working with Package Servers: browse the packages on a server, download them, upload `.thp` files, and copy packages from one server to another. Use it to move packages between a development and a production package server, or to manage the packages of a server that runs `TrakHound.Packages.Api`.

It doesn't install packages on the Instance it runs on. Packages are installed in the Admin interface (Packages) or with the CLI: see https://www.trakhound.com/docs/trakhound-framework/packages.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Packages.App` |
| Category | `app` |
| Default Name | Package Manager |
| Default Route | `trakhound/packages` |

Installing the package from the Admin interface (Packages) adds an App configuration with the route `trakhound/packages`. The CLI (`th packages install`) only installs the files unless you add `--configure`.

The App has no configuration parameters. Its Package Servers are saved in its Volume (see Servers).

## Servers
A Package Server is a server that serves the Packages Api (`TrakHound.Packages.Api`) at `<Base URL>/packages`, with an Organization. Add servers on the **Servers** tab with **Add Server**:

| Field | Required | Description |
|---|---|---|
| Name | Yes | The name shown in the App (ex. `TrakHound Public`) |
| Base URL | Yes | A full `http://` or `https://` URL (ex. `https://packages.trakhound.com` or `http://localhost:8472`) |
| Organization | Yes | The Organization that packages are read from and uploaded to (ex. `TrakHound`) |
| Access Token | No | Sent in the `X-TrakHound-Api` header, for servers or Organizations that require one |

**Test Connection** (and **Test** or **Test All** in the list) reads the latest packages of the Organization and shows how many there are, or that the server is unreachable. **Edit** changes a server and **Remove** removes it from the list. Removing a server doesn't change the packages on it.

The servers are saved to `servers.json` in the App's Volume (the `volumeId` of its App configuration, or its configuration ID). **The Access Tokens are saved in plain text** in that file, so limit who can read the Volume and who can use the App. If the App has no Volume, servers can be added but aren't saved.

The requests to Package Servers are made by the Instance, not the visitor's browser, so each Base URL must be reachable from the Instance.

## Packages
The **Packages** tab lists the latest version of each package on the selected server, with its description, category, version and build date.

- Search by package ID or description, and filter by category.
- Click a version to pick another one. The build date shown is for the selected version.
- **Download** saves one package as `<id>-<version>.thp`. Selecting several and clicking **Download** saves them in one `.zip` file.
- **Copy** (or **Copy to..** for the selected packages) copies the selected versions to another server. A server with the same Base URL and Organization isn't offered as a target.

## Upload
The **Upload** tab uploads `.thp` files to the selected server. Drop files or click to select them: up to 50 files at a time, each up to 512 MB. The App reads each file's ID, version, category and build date, marks files that aren't valid packages, and checks whether each version already exists on the server (**New** or **Exists on Server**).

## Overwriting
When an uploaded or copied version already exists on the target server, the App asks whether to **Overwrite** it (replacing the existing build), **Skip** it, or, for several packages, **Cancel Remaining**. It shows the build dates of both and notes when they're the same build. Tick **Apply to the remaining Packages that already exist** to use the same answer for the rest of the batch.

## Access
The App can upload and overwrite packages with the Access Tokens it holds, so anyone who can open it can change the packages on those servers. Protect its route with a Security Profile (Config > Access > Security Profiles in the Admin interface).

## Related Modules
| Package | Description |
|---|---|
| `TrakHound.Packages.Api` | The Packages Api that Package Servers run |
| `TrakHound.Packages.Retention.Function` | Deletes older versions of each package on the Instance's Packages Api |

## Development
`Program.cs` runs the App outside an Instance with `TrakHound.Debug.Apps`, using a local Volume for `servers.json`. The App's calls go to the Package Servers you add, so it works without an Instance. See https://www.trakhound.com/docs/trakhound-framework/debugging.

```bash
dotnet run --project TrakHound.Packages.App.csproj
```
