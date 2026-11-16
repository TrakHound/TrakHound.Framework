# TrakHound.FileSystem.Service
FileSystem Service for Blobs in the TrakHound Framework.

This service module implements `ITrakHoundBlobService` using the local file system. Blobs are organized into **collections** (one directory per collection), and each blob is stored as a file alongside a JSON file containing its key, content type, and metadata.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.FileSystem.Service` |
| Category | `service` |
| Service Name | `filesystem-local` |
| Service Type | Blobs (`ITrakHoundBlobService`) |
| Buffer Enabled | `false` |
| Target Framework | `net8.0` |

## Configuration
| Parameter | Default | Description |
|---|---|---|
| `directory` | `files/blobs` | The directory the blobs are stored in. Relative paths are resolved against the application's base directory. It is created if it does not exist. |

For example, with the default configuration blobs are stored in `{AppBaseDirectory}/files/blobs`, and with `directory: D:\TrakHound\blobs` they are stored in `D:\TrakHound\blobs`.

## Storage
Each blob is written to `{directory}/{collection}/` as two files:

| File | Description |
|---|---|
| `{hash}` | The blob content, where `{hash}` is `SHA256(key)` |
| `{hash}.metadata.json` | The blob information (key, content type, and metadata) |

```
files/
└── blobs/
    └── trakhound.packages/
        ├── 3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b
        └── 3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b.metadata.json
```

Hashing the key allows any string (including characters that are invalid in file names) to be used as a blob key. The original key is preserved in the metadata file.

### Metadata File
```json
{
  "key": "my-blob-key",
  "contentType": "application/octet-stream",
  "metadata": {
    "name": "value"
  }
}
```

## Operations
| Method | Description |
|---|---|
| `CheckAvailability()` | Available when the directory exists |
| `ListCollections()` | Lists each collection directory along with its blob count |
| `ListKeys(collection, skip, take)` | Lists the keys of the blobs in a collection |
| `List(collection, skip, take)` | Lists the blobs (key, content type, size, created, metadata) in a collection |
| `Read(collection, key)` | Gets the information for a single blob |
| `Download(collection, key)` | Opens a read-only `Stream` to the blob content |
| `Upload(collection, key, content, contentType, metadata)` | Writes the blob content and metadata file. Existing blobs with the same key are overwritten. The collection directory is created if needed. |
| `Delete(collection, key)` | Deletes the blob content and metadata file |

Blob `Size` is the size of the content file and `Created` is the file's creation time (Unix time).

## Response Codes
| Code | Description |
|---|---|
| 200 | Success |
| 400 | Missing parameters (ex. collection, key, content type, or content) |
| 404 | Directory, collection, or blob not found |
| 500 | File system error |

## Publishing
Package information is defined in `trakhound.package.json`. Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see [Versions from Git](https://www.trakhound.com/docs/trakhound-framework/packages#package-versions)).
