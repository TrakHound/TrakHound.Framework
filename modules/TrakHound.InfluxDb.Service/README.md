# TrakHound.InfluxDb.Service
InfluxDB 3 Time Series Services for the TrakHound Framework.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.InfluxDb.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `InfluxDbTimeSeriesService` | Time Series (`ITrakHoundTimeSeriesService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `url` | Yes | | The server's address (ex. `http://localhost:8181`) |
| `database` | No | `trakhound` | The database to use. The first write creates it |
| `token` | No | | The token, for a server that uses authentication. It needs to read and write the database |
| `timeout` | No | `30000` | How long to wait for a request, in milliseconds |
| `maxTimeWeightedPoints` | No | `1000000` | The most Points one `Aggregate` request can read into memory for `TimeWeightedMean` and `DurationInState`. A request that needs more returns 400 |

It's tested against InfluxDB 3 Core 3.12. InfluxDB 1.x and 2.x use different APIs and aren't supported.

## What's different from other Time Series Services
- **`Delete` returns 405**, since InfluxDB 3 can't delete Points, only a whole Table or Database. A Service Router tries its next Target. Use the database's retention period to remove old Points.
- **Line protocol limits:** a Tag Value can't contain a line break or end with a backslash. These return 400.
- **A Tag and a Field can't share a name** within a Measurement. The server returns an error, which the Service returns as 400.
- **`TimeWeightedMean` and `DurationInState` run in the Service**, since InfluxDB's SQL can't split a Value across Intervals. They read every Point in the range into memory, up to `maxTimeWeightedPoints` for each request. Several requests at once can each use that much.
- **Batches with whole numbers read the Measurement's Columns first**, to see whether a Field is a double. Two writers creating the same new Field at once can still conflict, so one gets a 400 and can retry.
- **Writes are checked by the server.** A batch with a bad line isn't written, but a large batch is split into requests of about 8 MB, and earlier requests stay written if a later one fails.

## Storage
Each Measurement is a Table. Tags and Fields are Columns, and each Field's Quality is another Field, named `$q_` and the Field's name. A Tag would make a Point with a new Quality a separate Series. Points written by something else have no Quality and are read as `Good`. Line protocol can't clear a Field, so a Field written without a value stores a Quality that is read as `Bad` with a null value, and a value written at that time before is ignored. Each Field uses two Columns, so count both toward the server's limit of Columns per Table.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Invalid names or values, a Field written with a different type, or a limit of the server (ex. the number of databases on Core) |
| `405` Route Not Configured | `Delete` |
| `503` Not Available | The server can't be reached, the token was rejected, or the server is overloaded (429) |
| `504` Timeout | The request timed out |
| `500` Internal Error | Any other error |

## Availability
`CheckAvailability()` calls the server's `health` endpoint, which needs the token on a server that uses authentication. The result is cached for 60 seconds.

## Performance
Measured with InfluxDB 3 Core 3.12, using the memory object store, in a Docker container on a Windows development machine, with default server settings. The test wrote 1,000,000 Points to 100 Series, with 5 Fields each, from 4 writers in batches of 5,000. Use these to compare changes, not to size a server.

| Operation | Time |
|---|---|
| Write | 20,098 Points/s |
| `Read` 1 Series, 1 day (288 Points) | 27 ms |
| `ReadLatest` 100 Series | 2,020 ms |
| `Aggregate` Mean, 1 Series, 34 days, hourly | 332 ms |
| `Aggregate` TimeWeightedMean, 1 Series, 34 days, hourly | 326 ms |
| `Aggregate` DurationInState, 100 Series, 34 days, daily | 7,960 ms |

Each write waits for the server to save it, so larger batches should write more Points per second than the same number of smaller ones. A request reads the Measurement's Columns first, which adds a small cost to each one.

## Tests
`tests/TrakHound.TimeSeries.Conformance.Tests` runs against a server when `TRAKHOUND_INFLUXDB_TEST_URL` is set (with `TRAKHOUND_INFLUXDB_TEST_TOKEN` if it uses authentication). Each test creates a database and drops it afterward. Set `TRAKHOUND_INFLUXDB_TEST_LOAD` to a number of Points to also run the load test. To run a server for the tests:

```bash
docker run -d --name trakhound-influxdb-test -p 127.0.0.1:8181:8181 influxdb:3-core influxdb3 serve --node-id test --object-store memory --without-auth
```
