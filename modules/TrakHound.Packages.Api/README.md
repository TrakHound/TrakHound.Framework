# TrakHound.Packages.Api
Download, Upload, & Manage Packages in the TrakHound Framework.

This API module acts as a package repository for a TrakHound Instance. Packages (`.thp` files built with `TrakHoundPackageBuilder`) are uploaded per **organization**, their information is indexed in a table, their contents are stored as blobs, and package information is cached in a key/value store for fast lookups.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Packages.Api` |
| Category | `api` |
| Default Route | `packages` |
| Target Framework | `net8.0` |

## Required Services
The module relies on the following TrakHound services being configured on the Instance:

| Service | Usage |
|---|---|
| **Tables** | Stores package information, metadata and dependencies. Queries use T-SQL syntax (`top`, `sys.views`, table variables), so a SQL Server based Tables service (ex. `TrakHound.SqlServer.Service`) is expected. |
| **Blobs** | Stores the package file contents in the `trakhound.packages` collection. |
| **KeyValues** | Caches package information (JSON) in the `trakhound.packages` collection. |

## Storage

### Tables
Tables are created automatically when the API is initialized.

**`[trakhound].[package]`**

| Column | Type | Primary Key |
|---|---|---|
| organization | string | ✔ |
| uuid | string | ✔ |
| id | string | |
| version_major | int | |
| version_minor | int | |
| version_patch | int | |
| version_prerelease | string | |
| category | string | |
| build_date | DateTime | |
| hash | string | |
| content_hash | string | |

The `content_hash` column is added to an existing table when the API is initialized. Packages uploaded before then have no value until they're uploaded again.

`version_prerelease` is a version's prerelease label (ex. `dev.20261114153000` for `0.3.4-dev.20261114153000`), and is empty for a stable version. It's added to an existing table when the API is initialized. A prerelease uploaded before then wasn't indexed (its version couldn't be read), so upload it again to list it. An upload whose version can't be read is refused (400).

**`[trakhound].[package_metadata]`**

| Column | Type | Primary Key |
|---|---|---|
| organization | string | ✔ |
| package_uuid | string | ✔ |
| name | string | ✔ |
| value | string (no length limit) | |

Metadata values have no length limit. Tables created before this was changed limited them to 255 characters: the `value` column of an existing table is widened when the API is initialized.

**`[trakhound].[package_dependency]`**

The packages each package depends on (the `dependencies` in its `package.json`). Packages uploaded before this table was added have no rows: upload them again to add their dependencies.

| Column | Type | Primary Key |
|---|---|---|
| organization | string | ✔ |
| package_uuid | string | ✔ |
| dependency_id | string | ✔ |
| version | string | |
| minimum_version | string | |

**`[trakhound].[package_latest_view]`**

A view (created from [Scripts/package_latest_view.sql](Scripts/package_latest_view.sql)) that returns the most recently **built** version (max `build_date`) of each package `id` per organization. It is used by the `latest` and `category` query endpoints.

### Blobs
Package contents are stored in the `trakhound.packages` blob collection using the key `SHA256("{organization}:{packageUuid}")`.

### Cache
Package information is cached in the `trakhound.packages` key/value collection using the key `{organization}:{packageUuid}`. Reads check the cache first and fall back to the tables, repopulating the cache on a miss. Uploading a package removes its cache entry, so a package uploaded again is read from the tables.

Package manifests are cached in the same collection using the key `{organization}:{packageUuid}:manifest`. They're read from the stored package file on the first request, so packages already stored need no migration. Uploading or deleting a package removes the entry. A manifest over 1 MB isn't cached, and is read from the package file for each request.

## Endpoints
All routes are relative to the API's route (default `packages`).

The `{organization}` in a route is matched case-insensitively: it is resolved to the casing the organization is stored with (an exact match wins if two organizations differ only by case). Uploading to a new organization stores it with the casing given.

### Organizations
| Method | Route | Description |
|---|---|---|
| GET | `organizations` | List all organizations that have packages |

### Read Packages
| Method | Route | Description |
|---|---|---|
| GET | `{organization}/{uuid}` | Get a package by its UUID |
| GET | `{organization}/{id}/{version}` | Get a specific version of a package (ex. `1.2.3` or `1.2.4-dev.20261114153000`). A version with a `*` wildcard returns the version an Instance would select: the highest matching stable version (ex. `1.*`, or `*` for any), else the highest matching prerelease. With `-*` (ex. `1.*-*`) the highest of either. |
| GET | `{organization}/{id}/latest` | Get the latest version of a package : the highest stable version, or the highest prerelease if it has none (the version `*` selects). Prereleases are compared in semantic versioning order, so `0.3.4-dev.20261114160000` is newer than `0.3.4-dev.20261114150000` |
| GET | `{organization}/{id}/versions` | List all versions of a package, including prereleases, oldest first in semantic versioning order |
| GET | `{organization}/latest?q={query}` | Get the latest version of all packages. The optional `q` parameter matches an exact package UUID or a package ID prefix. |
| GET | `{organization}/category/{category}?q={query}` | Same as above, filtered by package category (ex. `api`, `app`, `function`, `service`) |

Package responses are returned as `TrakHoundPackageJson`:

```json
{
  "uuid": "...",
  "id": "TrakHound.Packages.Api",
  "version": "0.2.13",
  "category": "api",
  "buildDate": "2026-01-01T00:00:00Z",
  "hash": "...",
  "metadata": {
    "publisher": "TrakHound Inc.",
    "description": "Download, Upload, & Manage Packages in the TrakHound Framework"
  }
}
```

### Download Packages
| Method | Route | Description |
|---|---|---|
| GET | `{organization}/{uuid}/download` | Download a package file by UUID |
| GET | `{organization}/{id}/{version}/download` | Download a package file by ID and version. A version with a `*` wildcard (ex. `1.*`) downloads the highest matching version, which is how Instances download a package's dependencies. |
| POST | `{organization}/download` | Download multiple packages as a `.zip` archive. *(Not yet implemented – currently returns 404)* |

Downloads are returned as `application/octet-stream` with a filename of `{id}.thp`.

### Read Manifests
| Method | Route | Description |
|---|---|---|
| GET | `{organization}/{id}/{version}/manifest` | Get the manifest and signature of a package version. A version with a `*` wildcard returns the highest matching version. |
| POST | `{organization}/manifests` | Get the manifests and signatures of up to 200 package versions. The request body is a JSON array of package UUIDs. A package that isn't found is left out of the result. |

These show who signed a package and what it was built from, without downloading it. They don't show that the package's files match the manifest: only checking the files does that, which an Instance always does when it installs a package.

The server doesn't check the signature, because each client decides who it trusts. `manifest` and `signature` are Base64 of the exact bytes in the package, since a signature only matches those bytes:

```json
{
  "uuid": "...",
  "manifest": "ewogICJmb3JtYXRWZXJzaW9uIjogMSwg...",
  "signature": "ewogICJhbGdvcml0aG0iOiAi..."
}
```

`signature` is empty for an unsigned package, and `manifest` is empty for a package without one. `error` is set if the manifest couldn't be read from the stored file, ex. it's over the 16 MB limit or the file isn't a package.

### Upload Packages
| Method | Route | Description |
|---|---|---|
| POST | `{organization}` | Upload a package file. The request body is the raw package file (`Content-Type: application/octet-stream`). |

The package information (ID, version, category, build date, hash, content hash, metadata and dependencies) is read from the `package.json` embedded in the uploaded file, written to the tables, and the file is stored as a blob. Uploading a version that's already stored replaces its information and its file. The uploaded package's information is returned on success.

```bash
curl -X POST "http://localhost:8472/packages/Public" \
     -H "Content-Type: application/octet-stream" \
     --data-binary "@TrakHound.Packages.Api-0.2.13.thp"
```

### Delete Packages
| Method | Route | Description |
|---|---|---|
| DELETE | `{organization}?packageUuid={uuid}` | Delete a single package version (blob, table rows, and cache entry) |
| DELETE | `{organization}/{packageId}` | Delete all versions of a package. *(Not supported – returns 500)* |

## Response Codes
| Code | Description |
|---|---|
| 200 | Success |
| 400 | Missing or invalid parameters (ex. invalid version string or package file) |
| 404 | Package, version, or organization not found |
| 500 | Error communicating with the Tables, Blobs, or KeyValues services |

## Development
The project can be run standalone for debugging using `TrakHound.Debug.AspNetCore`. [Program.cs](Program.cs) connects to a TrakHound Instance at `localhost:8472`, maps the API endpoints and streams, and serves the Api Debugger at `/_debug`.

```bash
dotnet run --project TrakHound.Packages.Api.csproj
```

> **Note:** [Service.cs](Service.cs) is legacy code that is excluded from compilation and kept for reference only.

The tests are in `tests/TrakHound.Packages.Api.Tests`. The endpoint tests need a SQL Server, and are skipped unless `TRAKHOUND_SQLSERVER_TEST_SERVER` names one (ex. `(localdb)\MSSQLLocalDB`). They create their own database and drop it after.

## Publishing
Package information is defined in [trakhound.package.json](trakhound.package.json). Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see the [CLI README](../../cli/README.md#versions)).
