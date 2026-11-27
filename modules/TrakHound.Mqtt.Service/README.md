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
| `hostname` | Yes | | Hostname or IP address of the MQTT Broker. Without it, every request returns `503` |
| `port` | No | `1883` (`8883` with TLS) | Port of the Broker |
| `clientId` | No | A new GUID | The Client ID of the connection used to Publish. It must be unique on the Broker |
| `username` | No | | The user to connect as. Leave it empty to connect without credentials |
| `password` | No | | The user's password, ex. `<mqtt-password>`. Only sent with `username` |
| `timeout` | No | `10000` | How long to wait (in milliseconds) to connect, and for each request to the Broker |
| `reconnectInterval` | No | `5000` | How long to wait (in milliseconds) before a Subscription reconnects after it was disconnected or its subscribe failed |
| `cleanSession` | No | `true` | Connect with the Clean Session flag. With `false`, the Broker keeps a Persistent Session for the Client ID: its Subscriptions, and the QoS `1` and `2` Messages that arrive while it's disconnected. This only applies to a connection with a set Client ID (`clientId` here, or the `clientId` of a `Subscribe()`); one with a generated Client ID always uses a Clean Session, since nothing would resume its session |
| `useTls` | No | `false` | Connect with TLS. The Broker's certificate must be valid. Setting `clientCertificate` or `caCertificate` also turns on TLS |
| `clientCertificate` | No | | The Client Certificate to authenticate with (mutual TLS): a PEM file (ex. `certificate.pem.crt`), a PKCS#12 file (`.pfx` or `.p12`) or the PEM text |
| `clientKey` | No | | The private key of a PEM `clientCertificate`: a PEM file (ex. `private.pem.key`) or the PEM text. Leave it empty when the certificate's PEM includes the key |
| `clientCertificatePassword` | No | | The password of a PKCS#12 `clientCertificate`, or of an encrypted `clientKey` |
| `caCertificate` | No | | The root CA Certificate(s) the Broker's certificate must chain to: a PEM file (one or more certificates), a DER file or the PEM text. When it's set, the certificates the system trusts aren't used and revocation isn't checked. Leave it empty for a Broker with a publicly trusted certificate |

The connections use MQTT 3.1.1. The QoS is set on each `Publish()` and `Subscribe()`.

## Certificates
- A relative file path starts at the Instance's working directory, so use a full path.
- The certificates are read once, when the Service is created. A file that's replaced (ex. a renewed certificate) is read when the Service configuration changes or the Instance restarts.
- If a certificate can't be read, the error is logged and every request returns `503` with the error.
- The Broker's certificate must always match `hostname`, with or without `caCertificate`.

For example, to connect to AWS IoT Core with the files from creating a Thing:

| Parameter | Value |
|---|---|
| `hostname` | The account's device data endpoint, ex. `<id>-ats.iot.<region>.amazonaws.com` |
| `clientId` | The Thing name (if the Thing's policy only allows its own Client ID) |
| `clientCertificate` | The full path of the Thing's `certificate.pem.crt` |
| `clientKey` | The full path of the Thing's `private.pem.key` |

The Amazon Root CA is publicly trusted, so `caCertificate` isn't needed. Port `443` (with ALPN) isn't supported, so use `8883`.

See [Services](https://www.trakhound.com/docs/trakhound-framework/services) for adding a Service configuration, and [Routing](https://www.trakhound.com/docs/trakhound-framework/routing) for sending Message requests to it.

To try it with a Broker, [examples/docker/mqtt](../../examples/docker/mqtt) runs an Instance and Mosquitto with Docker Compose, with this Service already configured.

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
- Delivery and Retained Messages are handled by the Broker. With a Clean Session (the default), Messages published while a Subscription is disconnected aren't kept for it. To keep them, set `cleanSession` to `false`, give the Subscription a `clientId`, and subscribe with QoS `1` or `2`.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Missing topic, topics or content, an invalid Topic, a QoS that isn't `0`, `1` or `2`, or a `clientId` that is already in use |
| `503` Not Available | `hostname` isn't set, or `Publish()` can't connect to the Broker |
| `500` Internal Error | The Broker refused the Message (the Broker's reason is returned), and any other error |

`Subscribe()` returns the stream without connecting, so a Broker that can't be reached doesn't return an error: the stream keeps trying to connect every `reconnectInterval`.

## Availability
`CheckAvailability()` opens the Publish connection (if it isn't open) and keeps it. It reports available once it's connected. The result isn't cached.

## Tests
`tests/TrakHound.Mqtt.Service.Tests` checks the connection options, the errors that don't need a Broker, and loading the certificates. It also connects with mutual TLS to a local TLS server that accepts the MQTT connection, to check that the Client Certificate is sent and the Broker is checked against `caCertificate`. There are no tests against a real Broker yet.
