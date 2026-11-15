# TrakHound.Example.Function

An example Function. It reads the readings that `TrakHound.Example.Worker` wrote to the shared Volume and returns the count, minimum, maximum and average for the last few minutes.

It shows how a Function describes its input Parameters (`[FunctionParameter]`), checks them, logs, and returns its result as Response Parameters (or `BadRequest` and `NotFound` with a message).

## Input Parameters

| Parameter | Default | Description |
| --- | --- | --- |
| `minutes` | `10` | How many minutes of readings to summarize |

## Response Parameters

`count`, `minimum`, `maximum`, `average`, `from` and `to` (the first and last reading's timestamp).

Use the Volume `trakhound.example` (the package default), the same as the Worker. `TrakHound.Example.Api` runs this Function from its `summary` endpoint.

See [the examples README](../README.md) for how the example packages work together.
