# TrakHound.Example.MqttToTimeSeries

An example Worker that subscribes to Messages (ex. from an MQTT Broker, through `TrakHound.Mqtt.Service`) and writes each value to a Time Series Service (ex. `TrakHound.Timescale.Service`). The Message's Topic gives the Point its Tags and its Field, so readings from many machines land in one Measurement, with a Series for each machine.

It shows how a Worker:

- subscribes to a Message stream through `Client.Services.Messages`, and subscribes again when the stream can't be opened or closes,
- turns Topics and payloads into Time Series Points (`TopicMapping` and `PayloadReader`, which have tests in `tests/TrakHound.Example.MqttToTimeSeries.Tests`),
- writes Points in batches through `Client.Services.TimeSeries`, keeps them while the Time Series Service can't be reached, and writes them once it can,
- logs a problem once instead of on every Message or retry.

The plant stack in [docker/plant](../docker/plant) runs it with simulated machines, Mosquitto and TimescaleDB.

## From Topic to Point

With the default `topicPattern` of `plant/{line}/{machine}/{field}`, the Worker subscribes to `plant/+/+/+`, and a Message

```
plant/line1/press-1/temperature   {"value": 64.2}
```

becomes a Point in the `machine` Measurement with the Tags `line=line1` and `machine=press-1`, and the Field `temperature` = `64.2`, at the time the Message Service received it.

Each level of `topicPattern` is one of:

| Level | Means |
| --- | --- |
| `{name}` | That level of the Topic is the value of the Tag `name` |
| `{field}` | That level of the Topic is the Field's name. Characters other than letters, numbers and underscores become `_` (ex. `spindle-speed` becomes `spindle_speed`). Without a `{field}` level, the Field is `value` |
| `+` | Any value, not used |
| `#` | Any number of levels, not used (only as the last level) |
| anything else | The Topic must have that exact level |

Keep Tags to values with a known, small set, such as lines and machines: each new combination of Tags is a new Series. A Topic that doesn't match the Pattern is skipped, and logged once.

## Payloads

A payload can be a number (`64.2`), `true` or `false`, or a JSON object with the value at `valuePath` (ex. `{"value": 64.2}`, or `{"data": {"temperature": 64.2}}` with `valuePath` set to `data.temperature`). Numbers are always written as doubles, so a Field's type doesn't depend on whether its first value had a decimal point. Any other payload is skipped, and logged once for its Topic.

## Configuration

| Parameter | Default | Description |
| --- | --- | --- |
| `topicPattern` | `plant/{line}/{machine}/{field}` | The Topics to subscribe to, and how they map to Tags and a Field (see above) |
| `measurement` | `machine` | The Measurement the Points are written to |
| `valuePath` | `value` | The property that holds the value in a JSON payload (properties separated by `.`) |
| `qos` | `0` | The QoS to subscribe with (`0`, `1` or `2`) |
| `clientId` | | The Client ID to subscribe with. Set it, with QoS 1 or 2 and the MQTT Service's `cleanSession: false`, so the Broker keeps the Messages that arrive while the Worker is stopped |
| `flushInterval` | `1000` | Milliseconds between Writes to the Time Series Service |
| `maxBuffer` | `10000` | How many Points to keep while the Time Series Service can't be reached. The oldest are dropped after that |

The Worker reaches the Message and Time Series Services through its Router (`routerId` in its Configuration, else `default`), so the Router needs a Route for `Messages` and one for `TimeSeries`. The plant stack's [Router](../docker/plant/config/router/default.yaml) has both.

## Debug it

`dotnet run` (or F5) runs the Worker on its own with the Parameters in `trakhound.debug.json`, using the Message and Time Series Services of the Instance at `instanceUrl` (with `apiKey`, if its Services need one).

See [the examples README](../README.md) for building and publishing the examples.
