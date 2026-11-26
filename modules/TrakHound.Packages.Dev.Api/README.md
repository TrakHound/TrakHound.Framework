# TrakHound.Packages.Dev.Api
A Package Server for development, with no services to set up.

It serves the same endpoints as `TrakHound.Packages.Api`, so `th packages push` uploads to it and an Instance's Package Sources browse and install from it the same way. It keeps each Package as the uploaded `.thp` file in its Volume instead of in Tables, Blobs and Key Values. It's for getting packages you're building into an Instance (for example one in a Docker container) without picking files in a browser. Use `TrakHound.Packages.Api` for a shared Package Server.

It only stores Packages. Installing one is still done by an Admin, from the Instance's **Packages** page, and the Instance checks it the same as a Package from any other Source (see [Package Verification](#package-verification)).

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Packages.Dev.Api` |
| Category | `api` |
| Default Route | `packages` |

## Setup
**In the Docker image**, start the container with `TRAKHOUND_DEV_UPLOAD_TOKEN` set to a long random value (at least 16 characters). It installs this package (it's included in the image), serves it at `packages` with that value as the `uploadToken` (read from the environment, never written to the volume), and adds the `Dev` Package Source. Starting the container without it removes the configuration again.

**Elsewhere:**

1. Install the package (Admin **Packages > Upload**) and add a configuration for it at the route `packages`.
2. Give uploads a credential (they're never anonymous, since an uploaded Package is code an Admin may install). Either:
   - set the `uploadToken` Parameter to a long random value, which `th packages push` sends from `TRAKHOUND_API_TOKEN` (or `apiToken` in `cli.config.yaml`), or
   - create an API Key in **Security > API Keys**, for clients that send `X-API-Key`.
3. Add a Package Source (**Packages > Sources**) for the Instance itself: Base URL `http://localhost:8472` (the Instance's own address, also inside a container) and an Organization, ex. `Dev`. Leave **Package Manager** off.

Then, from your project:

```powershell
th packages pack .\Contoso.Hello --output .\out --version-suffix dev.1
$env:TRAKHOUND_API_TOKEN = "<uploadToken>"
th packages push .\out\Contoso.Hello-1.0.0-dev.1.thp -s http://localhost:8472 --organization Dev
```

The Package is listed on the Instance's **Packages** page under the Source, ready to install.

## Parameters
| Parameter | Default | Description |
|---|---|---|
| `uploadToken` | (none) | The token `th packages push` sends (in the `X-TrakHound-Api` Header) to upload and delete. Without it, only a request with an Instance identity (ex. an API Key) can upload |
| `allowOverwrite` | `false` | Allow uploading a Version that's already stored, which replaces it. Otherwise it's refused (409), since an Instance doesn't see the same Version as an update: give each build a new Version, ex. with `--version-suffix` |
| `keepVersions` | `0` (all) | Keep only the newest N Versions of each Package (in Semantic Versioning order). Older Versions are deleted after each upload |
| `maxUploadSize` | `256` | The largest Package file accepted, in MB (413 when larger) |

## Storage
Packages are kept in the Api's Volume as the uploaded files:

```
packages/
  {organization}/
    {packageId}/
      1.2.0.thp
      1.2.1-dev.1.thp
```

- The Organization comes from the URL and is matched without regard to case. The ID and Version come from the Package file, never from the request, and are checked before they're used as names.
- A file is written to a temporary name first and moved into place, so a failed upload never leaves part of a Package.
- The files are the only record: the Api reads each file's information into memory, and reads the directory again at most every 2 seconds. A `.thp` file copied in by hand (ex. with `docker cp`) anywhere under `packages/{organization}/` is listed too. A file that isn't a Package is ignored.
- Uninstalling the package doesn't delete the Volume.

## Moving to a Package Server
Since the files are the uploaded Packages, upload each one to the other server (signatures and content hashes are unchanged):

```powershell
Get-ChildItem <volume>\packages\Dev -Recurse -Filter *.thp | ForEach-Object { th packages push $_.FullName -s https://packages.contoso.com --organization Contoso }
```

Then point the Instance's Package Source at the new server, or serve `TrakHound.Packages.Api` at the same route, and uninstall this package.

## Package Verification
A Package from this Api is checked like any other, by the Instance's **Package Verification** setting:

| Mode | An unsigned Package from this Api |
|---|---|
| Off, Warn (the default) | Installed (Warn shows it as Unsigned) |
| Require | Refused unless signed by a Trusted Publisher |
| RequireApproval | Refused: this Api isn't a Package Manager, so it can't approve a build |

Changing the mode later also applies to Packages already installed, when the Instance next starts.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `organizations` | The organizations that have packages |
| GET | `{organization}/{uuid}` | A package version by its UUID |
| GET | `{organization}/{id}/{version}` | A version (`1.2.3`, `1.*`, `*`, `*-*`) |
| GET | `{organization}/{id}/latest` | The highest stable version (or prerelease if there's none) |
| GET | `{organization}/{id}/versions` | Every version, oldest first |
| GET | `{organization}/latest` | The most recently built version of each package (`q` filters by UUID or ID prefix) |
| GET | `{organization}/category/{category}` | As `latest`, for one category |
| GET | `{organization}/{uuid}/download` | The package file |
| GET | `{organization}/{id}/{version}/download` | The package file |
| GET | `{organization}/{id}/{version}/manifest` | The manifest and signature |
| POST | `{organization}/manifests` | Manifests of up to 200 package UUIDs |
| POST | `{organization}` | Upload a package file (`application/octet-stream`) |
| DELETE | `{organization}?packageUuid=` | Delete a version |
| DELETE | `{organization}/{packageId}` | Delete every version |

Uploads and deletes have the `write` Permission and reads have `read`, so a Security Profile can limit them further. A resource that no Profile covers is open, which is why uploads also check for a credential themselves.
