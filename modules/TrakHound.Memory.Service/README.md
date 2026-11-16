# TrakHound.Memory.Service
In Memory Key-Value, Message and Message Queue Services for the TrakHound Framework.

This service module implements `ITrakHoundKeyValueService`, `ITrakHoundMessageService` and `ITrakHoundMessageQueueService` using in-process memory. Since all data is held in memory, nothing is persisted and everything is lost when the service is stopped or restarted.

- **Key-Values** : entries are organized into **collections** and are automatically removed after a configurable expiration time
- **Messages** : an in-process publish/subscribe bus with MQTT-style topics, wildcards and retained messages
- **Message Queues** : Queues where each message is delivered to one consumer and kept until it is acknowledged. Use it to develop and test packages that use Message Queues without a broker

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Memory.Service` |
| Category | `service` |
| Service Types | Key-Values (`ITrakHoundKeyValueService`), Messages (`ITrakHoundMessageService`), Message Queues (`ITrakHoundMessageQueueService`) |
| Target Framework | `net8.0` |

## Configuration
| Parameter | Type | Default | Description |
|---|---|---|---|
| `ttl` | `duration` | `5m` | (Key-Values) Time after an entry is published before it expires, when `Publish()` isn't passed a `ttl` |
| `expirationInterval` | `duration` | `1m` | (Key-Values) How often expired entries are removed |
| `bufferSize` | `int` | `1000` | (Messages) The maximum number of messages queued for each subscriber before the oldest are dropped |
| `maxQueueLength` | `int` | `10000` | (Message Queues) The most messages a Queue holds (ready and unacknowledged). Publishing to a full Queue returns `409` |
| `prefetch` | `int` | `100` | (Message Queues) The most unacknowledged messages sent to a Subscription at once, when it doesn't set its own |

Durations use the TrakHound duration format (ex. `30s`, `5m`, `1h30m`, `2 hours`, or a TimeSpan such as `00:05:00`). A bare number is read as **nanoseconds**, so always include a unit. Missing, invalid or zero values use the default.

# Key-Values

## Storage
Entries are identified internally by `{collection}:{key}` (lowercase), so collection names and keys are **case-insensitive**. Publishing a key that already exists replaces its value.

### Expiration
| Setting | Parameter | Default | Description |
|---|---|---|---|
| TTL | `ttl` | 5 Minutes | Time after an entry is published before it expires |
| Interval | `expirationInterval` | 1 Minute | How often expired entries are removed |

A `ttl` passed to `Publish()` overrides the configured TTL for those entries. The expiration time is measured from when an entry was last published (reading an entry does not extend it). Publishing the key again resets the expiration. When an entry expires, its key is also removed from the collection, and a collection is no longer listed once it contains no keys.

Expired entries can't be read, but until the next expiration interval runs their keys may still be listed by `ListCollections()`, `ListKeys()` and `Count()`.

## Operations
| Method | Description |
|---|---|
| `ListCollections()` | Lists the names of all collections that currently contain keys |
| `ListKeys(collection, pattern, skip, take)` | Lists the keys in a collection. Use `*` to return all keys, or a trailing wildcard (ex. `sensor*`) to filter by prefix. `skip` and `take` are currently not applied. |
| `Read(collection, keys)` | Gets the values for the specified keys. Keys that are not found (or have expired) are returned with a `null` value. |
| `Count(collection)` | Gets the number of keys in a collection |
| `Publish(collection, entries, ttl)` | Adds or replaces the specified key-value entries. `ttl` (optional) overrides the configured TTL for these entries. |
| `Delete(collection, keys)` | Removes the specified keys from a collection |

All operations are thread-safe.

## Response Codes
| Code | Description |
|---|---|
| 200 | Success |
| 400 | Missing parameters (ex. collection, pattern, keys, or entries) |
| 404 | Collection or keys not found |

# Messages

## Topics
Topics use MQTT syntax: levels are separated by `/`, and topics are **case-sensitive**.

| Filter | Matches |
|---|---|
| `a/b` | Only `a/b` |
| `a/+` | Exactly one level below `a` (ex. `a/b`, but not `a/b/c`) |
| `a/#` | `a` and any number of levels below it (ex. `a`, `a/b`, `a/b/c`) |

`+` must be an entire level and `#` must be the entire last level. Wildcards are not allowed when publishing. A subscriber whose filters overlap receives each message only once.

## Delivery
Each subscriber has its own bounded buffer (see `bufferSize`), and messages are delivered to it on a background task. Publishing never waits for subscribers, so a slow subscriber can't block publishers or other subscribers. When a buffer is full, the **oldest** queued message is dropped.

Messages published after `Subscribe()` returns but before the stream is started are queued and delivered once `Start()` is called.

## Retained Messages
Publishing with `retain` set stores the message as the retained message for its topic, replacing any previous one. New subscribers receive the retained messages that match their filters before any live messages. Retained messages are delivered with `Retain = true`, and live messages with `Retain = false`.

Publishing an empty payload with `retain` set clears the retained message for that topic. There is currently no limit on the number of retained topics.

## QoS
QoS 0, 1 and 2 are accepted and each message is delivered with the lower of the publish and subscribe QoS. Delivery is best-effort for every level: messages published while there are no matching subscribers (other than retained messages) are not stored, and acknowledgements and persistent sessions are not implemented.

The `clientId` parameter of `Subscribe()` is used only for logging. It doesn't need to be unique, and subscribers with the same ID don't replace each other.

## Scope
Each service configuration is a separate message bus. When a configuration changes, the service is replaced: all of its subscriber streams are disposed and its retained messages are cleared.

## Message Operations
| Method | Description |
|---|---|
| `Subscribe(clientId, topics, qos)` | Subscribes to one or more topic filters and returns a stream of messages. Call `Start()` on the stream to begin delivery, and `Dispose()` to unsubscribe. |
| `Publish(topic, content, retain, qos)` | Publishes a message to all matching subscribers. An empty payload is valid. |

## Message Response Codes
| Code | Description |
|---|---|
| 200 | Success |
| 400 | Missing topic or content, invalid topic or filter, or QoS not 0, 1 or 2 |

# Message Queues

## Queues
A Queue is created the first time it is used, and Queue names are case-sensitive. Each service configuration has its own Queues, and they are cleared when the service is replaced (when its configuration changes) or the Instance restarts.

## Delivery
- Each message is delivered to one consumer: a `Pull`, or one of the Queue's Subscriptions in turn.
- Without `autoAcknowledge`, a delivered message stays on the Queue until it is Acknowledged (removed) or Rejected (returned to the front of the Queue with `Redelivered` set, or discarded with `requeue: false`). A Subscription is sent at most `prefetch` unacknowledged messages at once.
- Stopping or disposing a Subscription returns its unacknowledged messages to the front of the Queue. Pulled messages are held until they are Acknowledged or Rejected.
- A Delivery ID works once. Acknowledging or Rejecting it again, or after its Subscription closed, returns a failed result for it.
- `PublishedTimestamp` is when the message was published (not whole seconds, unlike RabbitMQ).

## Metrics
`GetMetrics` returns the counts and totals of each Queue. Rates are not calculated (they are null).

## Message Queue Response Codes
| Code | Description |
|---|---|
| 200 | Success (`Acknowledge` and `Reject` return a result for each Delivery ID) |
| 400 | Missing Queue, content or Delivery IDs |
| 409 | The Queue is full (`maxQueueLength`) |

## Publishing
Package information is defined in `trakhound.package.json`. Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see [Versions from Git](https://www.trakhound.com/docs/trakhound-framework/packages#package-versions)).
