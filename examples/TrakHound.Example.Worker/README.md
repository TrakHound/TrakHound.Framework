# TrakHound.Example.Worker

An example Worker. It reads a simulated temperature sensor (20 ± 5 over a 10 minute cycle, with some noise) at an interval and writes the most recent readings to `readings.json` in its Volume. `TrakHound.Example.Api` and `TrakHound.Example.Function` read them from there.

It shows how a Worker starts a loop in `OnStartAsync`, stops it in `OnStopAsync`, reads its Configuration parameters, logs, and keeps running when one iteration fails.

## Configuration

| Parameter | Default | Description |
| --- | --- | --- |
| `interval` | `5000` | Milliseconds between readings |
| `historySize` | `720` | How many readings to keep (720 is 1 hour at the default interval) |

Use the Volume `trakhound.example` (the package default) so the Api and Function can read the readings.

See [the examples README](../README.md) for how the example packages work together.
