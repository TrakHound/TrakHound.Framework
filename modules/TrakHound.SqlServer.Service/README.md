# TrakHound.SqlServer.Service
SQL Server Services for the TrakHound Framework.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.SqlServer.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `SqlServerTableService` | Tables (`ITrakHoundTableService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `server` | Yes | | Hostname (or `host\instance`) of the SQL Server |
| `port` | No | | TCP port of the SQL Server (the SQL Server default when not set) |
| `database` | No | | The database to connect to (the login's default database when not set) |
| `user` | No | | SQL Server login. Windows (Integrated) authentication is used when not set |
| `password` | No | | Password for the `user` login |
| `connectionTimeout` | No | `10000` | How long to wait for a connection, in milliseconds (rounded up to whole seconds) |
| `trustServerCertificate` | No | `true` | Accept the server's certificate without validating it. Set to `false` when the server has a trusted certificate |

To try it with a server, [docker/compose/sqlserver](../../docker/compose/sqlserver) runs an Instance and SQL Server with Docker Compose, with this Service already configured.

## Queries
- The Service runs the `sqlserver` version of a Query Variant, then the `ansi` version.
- A Query is split into batches on `GO` lines (not inside strings or comments), and each batch runs as its own command on one connection. The `timeout` (in seconds, default 60) applies to each batch.
- `GetColumns` takes a Table or View name (ex. `schema.table`) and returns 404 if it doesn't exist.

## Errors
SQL errors are returned with a Status Code that tells a Service Router whether to try the next Target. Errors in the Query itself are never sent to the next Target, so a Query isn't run twice.

| Status Code | SQL errors |
|---|---|
| `400` Bad Request | Errors in the Query (ex. syntax, unknown table or column, conversion, null or truncation errors) |
| `409` Conflict | Constraint violations (duplicate key, foreign key, check) |
| `503` Not Available | The server can't be reached, the login failed, or the Query was chosen as a deadlock victim (and rolled back) |
| `504` Timeout | The command timed out |
| `500` Internal Error | Any other error |

A script that fails partway may already have committed its earlier `GO` batches, so a `503` or `504` from a multi-batch write can mean part of it ran.

## Availability
`CheckAvailability()` connects and runs `select 1`. The result (including the error message when it fails, ex. a failed login) is cached for 60 seconds.

## Tests
`tests/TrakHound.SqlServer.Service.Tests` runs without a database. Its integration tests also run when `TRAKHOUND_SQLSERVER_TEST_SERVER` is set (with `TRAKHOUND_SQLSERVER_TEST_PORT`, `_DATABASE`, `_USER` and `_PASSWORD`; Windows authentication when no user is set). They create and then drop a `trakhound_tests` schema, so use a development database. `tests/TrakHound.Tables.Conformance.Tests` runs its shared tests on SQL Server with the same settings, using a `trakhound_conformance` schema.
