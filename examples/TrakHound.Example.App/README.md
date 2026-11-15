# TrakHound.Example.App

An example App (default route `example`). Its page shows the latest reading and the recent readings from `TrakHound.Example.Api`, refreshed every 2 seconds, and a Summarize button that gets the last 10 minutes' summary from the Api (which runs `TrakHound.Example.Function`).

It shows how an App page inherits `TrakHoundComponentBase`, reads its Configuration parameters, calls an Api through the Client, refreshes on a timer and stops it when the page closes.

The package depends on `TrakHound.Example.Api`, `TrakHound.Example.Function` and `TrakHound.Example.Worker`, so installing it from Packages in the Admin interface installs and configures all four.

## Configuration

| Parameter | Default | Description |
| --- | --- | --- |
| `apiRoute` | `example/api` | The route of the `TrakHound.Example.Api` configuration |

See [the examples README](../README.md) for how the example packages work together.
