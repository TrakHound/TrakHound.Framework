# TrakHound.Mqtt.Service
MQTT Message Services for the TrakHound Framework. Use it to publish and subscribe to Messages on an MQTT Broker (ex. Mosquitto, EMQX or HiveMQ), for example to share data with devices and other applications that use MQTT.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Mqtt.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `MqttMessageService` | Messages (`ITrakHoundMessageService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `hostname` | Yes | | Hostname or IP address of the MQTT Broker |
| `port` | No | `1883` (`8883` with `useTls`) | Port of the Broker |
| `clientId` | No | A new GUID | The Client ID of the connection used to Publish. It must be unique on the Broker |
| `username` | No | | The user to connect as. Leave it empty to connect without credentials |
| `password` | No | | The user's password, ex. `<mqtt-password>`. Only sent with `username` |
| `timeout` | No | `10000` | How long to wait (in milliseconds) to connect, and for each request to the Broker |
| `reconnectInterval` | No | `5000` | How long to wait (in milliseconds) before a Subscription reconnects after it was disconnected or its subscribe failed |
| `cleanSession` | No | `false` | Connect with the Clean Session flag. Currently every connection uses a clean session, even with `false`, so the Broker doesn't keep a persistent session |
| `useTls` | No | `false` | Connect with TLS. The Broker's certificate must be valid |
| `qos` | No | `0` | Not used: the QoS is set on each `Publish()` and `Subscribe()` |

The connections use MQTT 3.1.1.

See [Services](https://www.trakhound.com/docs/trakhound-framework/services) for adding a Service configuration, and [Routing](https://www.trakhound.com/docs/trakhound-framework/routing) for sending Message requests to it.

## Connections
- The Service has one connection for Publishing. It is opened on the first `Publish()` (or availability check) and kept open, and it reconnects on the next `Publish()` after a disconnection.
- Each Subscription has its own connection. Once its stream is started, it connects, subscribes to its Topics and stays connected until it is stopped or disposed. After a disconnection, or when the Broker rejects any of its Topics, it disconnects, waits `reconnectInterval` and tries again.
- A Broker allows one connection per Client ID. `Subscribe()` refuses a `clientId` that another open Subscription of the Service is using, or that is the `clientId` of the Publish connection. A Subscription without a `clientId` gets a new GUID.
- When the configuration changes, the Service is replaced and its Publish connection is closed.

## Topics
Topics use MQTT syntax, and the Broker does the matching: levels are separated by `/`, `+` matches one level and `#` matches any number of levels at the end. Wildcards are allowed in `Subscribe()` and not in `Publish()`. Topics are checked before they are sent to the Broker, and an invalid Topic returns `400`.

## Messages
- `Publish(topic, content, retain, qos)` sends the content as the payload, with the `retain` flag and QoS (`0`, `1` or `2`). An empty payload is valid, ex. to clear a Retained Message. With QoS `1` or `2`, `Publish()` waits for the Broker to acknowledge the Message.
- `Subscribe(clientId, topics, qos)` returns a stream for the Topics, each subscribed with the QoS. Call `Start()` on the stream to connect and begin delivery, and `Dispose()` to unsubscribe and disconnect.
- A received Message has its Topic, payload, `Retain` flag and QoS. Its `Timestamp` is when the Service received it, since MQTT Messages don't carry one.
- Delivery and Retained Messages are handled by the Broker. Because each connection uses a clean session, Messages published while a Subscription is disconnected aren't kept for it.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Missing topic, topics or content, an invalid Topic, a QoS that isn't `0`, `1` or `2`, or a `clientId` that is already in use |
| `503` Not Available | The Service has no configuration |
| `500` Internal Error | The Broker can't be reached, or it refused the Message (the Broker's reason is returned), and any other error |

`Subscribe()` returns the stream without connecting, so a Broker that can't be reached doesn't return an error: the stream keeps trying to connect every `reconnectInterval`.

## Availability
`CheckAvailability()` opens the Publish connection (if it isn't open) and keeps it. It reports available once it's connected. The result isn't cached.

## Tests
This package has no automated tests yet.
