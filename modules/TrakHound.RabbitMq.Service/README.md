# TrakHound.RabbitMq.Service
RabbitMQ Services for the TrakHound Framework. Use it when Messages must not be lost: each Message is stored by the Broker until a consumer Acknowledges it.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.RabbitMq.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `RabbitMqMessageQueueService` | Message Queues (`ITrakHoundMessageQueueService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `hostname` | No | `localhost` | Hostname of the RabbitMQ Broker |
| `port` | No | `5672` (`5671` with `ssl`) | AMQP port of the Broker |
| `virtualHost` | No | `/` | The Virtual Host to use |
| `username` | No | `guest` | The user to connect as (RabbitMQ only allows `guest` from the Broker's own machine) |
| `password` | No | `guest` | The user's password |
| `ssl` | No | `false` | Connect with TLS. The Broker's certificate must be valid for `hostname` |
| `connectionTimeout` | No | `5000` | How long to wait (in milliseconds) to connect, and for the Broker to confirm a published Message |
| `prefetch` | No | `100` | The most unacknowledged Messages sent to a Subscription at once, when it doesn't set its own |
| `publishConfirm` | No | `true` | Wait for the Broker to confirm that each published Message was stored. Set to `false` to publish faster, but a Message the Broker drops is still reported as published |
| `managementPort` | No | `15672` (`15671` with `ssl`) | Port of the management plugin's HTTP API (used for Metrics) |
| `managementUrl` | No | `http://{hostname}:{managementPort}` | Base URL of the management API, if it isn't at the default address |

## Queues
- A Queue is created (durable) the first time it is used. A Queue that already exists with other settings (ex. a quorum Queue, or one with a TTL or a dead letter exchange) is used as it is.
- Messages are published as persistent, so they are kept when the Broker restarts.

## Delivery
- `Pull` and `Subscribe` without `autoAcknowledge` leave each Message on the Queue until it is Acknowledged or Rejected. If the connection closes first, the Broker delivers the Message again with `Redelivered` set.
- `Reject` with `requeue: false` discards the Message, or sends it to the Queue's dead letter exchange if it has one.
- `Acknowledge` and `Reject` return a result for each Delivery ID. A Delivery ID stops working once the connection or channel it was delivered on closes (ex. a Subscription was disposed, or the Broker restarted).
- Pulled Messages are held on a channel shared by every Pull of the Service, which is separate from the channel used to Publish.
- `MessageId`, `ContentType` and `Headers` are sent as AMQP properties. A Message published without a `MessageId` gets a new GUID, and `Publish` returns the Message ID. `PublishedTimestamp` is the AMQP timestamp, which is whole seconds. Header values from other publishers are converted to strings.

## Metrics
Metrics come from the management plugin (`rabbitmq_management`), which lists every Queue with its Message rates. If the management API can't be reached, Metrics only have the Ready and Consumer counts of the Queues this Service has used (or the ones requested), and the API is tried again after a minute.

## Testing
`tests/TrakHound.MessageQueues.Conformance.Tests` (the behavior every Message Queue Service shares) and `tests/TrakHound.RabbitMq.Service.Tests` (what only this Service does) run against a Broker when `TRAKHOUND_RABBITMQ_TEST_SERVER` is set (with `TRAKHOUND_RABBITMQ_TEST_PORT`, `TRAKHOUND_RABBITMQ_TEST_USERNAME` and `TRAKHOUND_RABBITMQ_TEST_PASSWORD` if needed). For example:

```bash
docker run -d --rm --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management
```

## Publishing
Package information is defined in `trakhound.package.json`. Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see [Versions from Git](https://www.trakhound.com/docs/trakhound-framework/packages#package-versions)).
