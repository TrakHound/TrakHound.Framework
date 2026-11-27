# TrakHound.Postgres.Service
PostgreSQL Services for the TrakHound Framework.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Postgres.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `PostgresTableService` | Tables (`ITrakHoundTableService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `server` | Yes | | Hostname of the PostgreSQL server |
| `port` | No | `5432` | TCP port of the server |
| `database` | No | The `user` name | The database to connect to |
| `user` | No | | The login |
| `password` | No | | Password for the `user` login |
| `connectionTimeout` | No | `10000` | How long to wait for a connection, in milliseconds (rounded up to whole seconds) |

To try it with a server, [examples/docker/postgres](../../examples/docker/postgres) runs an Instance and a PostgreSQL (TimescaleDB) server with Docker Compose, with this Service already configured.

## Queries
- The Service runs the `postgres` version of a Query Variant, then the `ansi` version.
- `GetColumns` takes a Table or View name (ex. `schema.table`) and returns 404 if it doesn't exist.
- Text comparisons are case-sensitive, unlike a default SQL Server database.
- Clustered Indexes aren't used, since PostgreSQL has none. `Include` Columns need PostgreSQL 11 or later.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target. Errors in the Query itself are never sent to the next Target, so a Query isn't run twice.

| Status Code | Errors (SQLSTATE) |
|---|---|
| `400` Bad Request | Errors in the Query (classes `22` and `42`, and null values, `23502`) |
| `409` Conflict | Constraint violations (other errors in class `23`) |
| `503` Not Available | The server can't be reached, the login failed, or the Query was rolled back as a deadlock victim or serialization failure |
| `504` Timeout | The server canceled the command (`57014`) |
| `500` Internal Error | Any other error |

## Availability
`CheckAvailability()` connects and runs `select 1`. The result (including the error message when it fails) is cached for 60 seconds.

## Tests
`tests/TrakHound.Tables.Conformance.Tests` runs against a server when `TRAKHOUND_POSTGRES_TEST_SERVER` is set (with `TRAKHOUND_POSTGRES_TEST_PORT`, `_DATABASE`, `_USER` and `_PASSWORD`). The tests drop and create the `trakhound_conformance` and `trakhound` Schemas, so use a development database.
