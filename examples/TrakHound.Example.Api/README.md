# TrakHound.Example.Api

An example Api (default route `example/api`). It serves the readings that `TrakHound.Example.Worker` writes to the shared Volume, runs `TrakHound.Example.Function` to summarize them, and reads and writes Instance Variables.

It shows how to describe endpoints and their parameters, mark the safe ones as MCP Tools (`[ApiMcpTool]`), return JSON, text and status codes, stream values (`[ApiStream]`), and call a Function through the Client.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| GET | `hello?name=` | A greeting with the Instance time |
| GET | `readings/latest` | The most recent reading |
| GET | `readings?take=` | The most recent readings, newest first |
| Stream | `readings` | Each new reading as the Worker writes it (WebSocket, or Server-Sent Events with `Accept: text/event-stream`) |
| GET | `summary?minutes=` | Runs `TrakHound.Example.Function` and returns its count, minimum, maximum and average |
| GET | `variables/{key}` | Reads a Variable |
| PUT | `variables/{key}?value=` | Sets a Variable |

## Configuration

| Parameter | Default | Description |
| --- | --- | --- |
| `functionId` | `TrakHound.Example.Function` | The Function the `summary` endpoint runs |

Use the Volume `trakhound.example` (the package default), the same as the Worker.

See [the examples README](../README.md) for how the example packages work together.
