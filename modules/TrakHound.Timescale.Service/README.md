# TrakHound.Timescale.Service
TimescaleDB Time Series Services for the TrakHound Framework.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Timescale.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `TimescaleTimeSeriesService` | Time Series (`ITrakHoundTimeSeriesService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `server` | Yes | | Hostname of the TimescaleDB server |
| `port` | No | `5432` | TCP port of the server |
| `database` | No | The `user` name | The database to connect to |
| `user` | No | | The login |
| `password` | No | | Password for the `user` login |
| `connectionTimeout` | No | `10000` | How long to wait for a connection, in milliseconds (rounded up to whole seconds) |
| `schema` | No | `trakhound_timeseries` | The Schema that holds the Measurements |
| `chunkDays` | No | `7` | How many days of Points each Chunk holds. Applies to new Measurements |
| `compressAfterDays` | No | `30` | Compresses Chunks older than this many days (`0` turns compression off). Applies to new Measurements |

To try it with a server, [docker/compose/postgres](../../docker/compose/postgres) runs an Instance and a TimescaleDB server with Docker Compose, with this Service already configured.

## Setup
- The first request creates the Schema and the `timescaledb` extension if they don't exist, which needs those permissions. If an administrator has already created the extension and Schema, the login only needs to create Tables in the Schema.
- Writing needs TimescaleDB 2.11 or later to update Points in compressed Chunks. It's tested against 2.31 on PostgreSQL 17.
- The Apache licensed edition of TimescaleDB has no compression. The Service logs a warning, and Points are stored uncompressed.

## Storage
- A `$series` Table has a row for each unique combination of Tags. These rows aren't deleted, since other Instances may have their IDs cached. `ListSeries` and `ListMeasurements` only return what still has Points.
- Each Measurement is a Hypertable named after it, with `time`, `$series` and two Columns for each Field: its value, and its Quality in `$q_` and the Field's name. A row has a Field when its Quality isn't null, and a Field that has never had a value has only its Quality Column.
- A new Field adds its Columns, so writers of that Measurement wait briefly while it's added. Later writes don't wait.

## Retention
The Service doesn't delete old Points. Add a TimescaleDB retention policy to the Measurements that need one:

```sql
select add_retention_policy('trakhound_timeseries.spindle', interval '2 years');
```

## Errors
Errors use the same Status Codes as `TrakHound.Postgres.Service`, so a Service Router knows whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Invalid names or values, a Field written with a different type, or a Function that can't run on a Field's type |
| `503` Not Available | The server can't be reached, the login failed, or the write was rolled back as a deadlock victim |
| `504` Timeout | The server canceled the command |
| `500` Internal Error | Any other error |

## Availability
`CheckAvailability()` connects and reads the version of the `timescaledb` extension. It's unavailable if the extension isn't enabled in the database. The result is cached for 60 seconds.

## Performance
Measured with TimescaleDB 2.31 on PostgreSQL 17, in a Docker container on a Windows development machine, with default server settings. The test wrote 1,000,000 Points to 100 Series, with 5 Fields each, from 4 writers in batches of 5,000, while a new Field arrived about once a second. Use these to compare changes, not to size a server.

| Operation | Uncompressed | Compressed |
|---|---|---|
| Write | 82,845 Points/s | Not measured |
| `Read` 1 Series, 1 day (288 Points) | 25 ms | 19 ms |
| `ReadLatest` 100 Series | 15 ms | 15 ms |
| `Aggregate` Mean, 1 Series, 34 days, hourly | 27 ms | 11 ms |
| `Aggregate` TimeWeightedMean, 1 Series, 34 days, hourly | 28 ms | 23 ms |
| `Aggregate` DurationInState, 100 Series, 34 days, daily | 1,183 ms | 1,074 ms |

The Hypertable was 24 MB after compression when each row had one Quality. It hasn't been measured since each Field got its own.

## Tests
`tests/TrakHound.TimeSeries.Conformance.Tests` runs against a server when `TRAKHOUND_TIMESCALE_TEST_SERVER` is set (with `TRAKHOUND_TIMESCALE_TEST_PORT`, `_DATABASE`, `_USER` and `_PASSWORD`). The tests drop and create the `trakhound_conformance_ts` and `trakhound_server_ts` Schemas, so use a development database. Set `TRAKHOUND_TIMESCALE_TEST_LOAD` to a number of Points to also run the load test. To run a server for the tests:

```bash
docker run -d --name trakhound-timescale-test -p 127.0.0.1:5432:5432 -e POSTGRES_PASSWORD=<password> timescale/timescaledb:latest-pg17
```
