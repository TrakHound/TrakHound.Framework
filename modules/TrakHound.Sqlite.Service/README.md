# TrakHound.Sqlite.Service
SQLite Services for the TrakHound Framework. Use it for small Instances and for development, where running a database server isn't worth it.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Sqlite.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `SqliteTableService` | Tables (`ITrakHoundTableService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `database` | No | `trakhound.db` | Path of the database file. A relative path starts at the Instance's working directory, so use a full path. The file and its folder are created if they don't exist |

## Schemas
SQLite has no Schemas, so each Schema is its own file next to the database, attached under the Schema's name. The `trakhound` Schema of `data.db` is `data.trakhound.db`. Back up all of the files together.

A write that spans more than one Schema isn't fully atomic if the process or machine crashes during the commit, because each file commits separately.

A View can only use Tables in its own file, so its script leaves the Schema off those Tables.

## Queries
- The Service runs the `sqlite` version of a Query Variant, then the `ansi` version.
- `GetColumns` takes a Table or View name (ex. `schema.table`) and returns 404 if it doesn't exist.
- Column lengths aren't enforced, and a Decimal keeps about 15 significant digits.
- Text comparisons with `=` are case-sensitive, unlike a default SQL Server database.
- Clustered Indexes and `Include` Columns aren't used.
- One write runs at a time, and other writes wait for up to the command's timeout. The Service uses WAL mode, so reads continue during a write.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target. Errors in the Query itself are never sent to the next Target, so a Query isn't run twice.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Errors in the Query (ex. syntax or unknown tables), and null values |
| `409` Conflict | Other constraint violations (duplicate key, foreign key or check) |
| `503` Not Available | The database is busy or locked, or the file can't be opened |
| `500` Internal Error | Any other error |

## Availability
`CheckAvailability()` opens the database and runs `select 1`. The result (including the error message when it fails) is cached for 60 seconds.

## Tests
`tests/TrakHound.Tables.Conformance.Tests` runs against SQLite without any setup.
