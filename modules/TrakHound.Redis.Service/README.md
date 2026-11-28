# TrakHound.Redis.Service
Redis Key-Value Services for the TrakHound Framework. Use it when Key-Values must be shared by more than one Instance, or kept when an Instance restarts.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Redis.Service` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `RedisKeyValueService` | Key-Values (`ITrakHoundKeyValueService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `server` | Yes | | Hostname or IP address of the Redis server. Without it the Service isn't configured, and every request returns `503` |
| `port` | No | `6379` | Port of the Redis server |
| `database` | No | `0` | The Redis database number to use |
| `user` | No | | The user to connect as (Redis 6 ACL users). Leave it empty to use the `default` user |
| `password` | No | | The password of the user, ex. `<redis-password>` |
| `useSsl` | No | `false` | Connect with TLS |

Connecting, and each command, time out after 5 seconds. The connection is opened on the first request and kept open, and it reconnects on its own if the server goes away.

See [Services](https://www.trakhound.com/docs/trakhound-framework/services) for adding a Service configuration, and [Routing](https://www.trakhound.com/docs/trakhound-framework/routing) for sending Key-Value requests to it.

To try it, [docker/compose/redis](../../docker/compose/redis) runs two Instances that share Key-Values through a Redis server with Docker Compose, with this Service already configured on both.

## Storage
- Each entry is a Redis string with the key `{collection}:{key}`. The Collection name is URL encoded (ex. `a:b` is stored as `a%3Ab`), so a `:` in a Collection name can't be confused with the start of the Key. Keys are stored as they are.
- Collection names and Keys are case-sensitive.
- Publishing a Key that already exists replaces its value.
- An entry only expires when `Publish()` is passed a `ttl`. The value and its expiration are set in the same command. Without a `ttl`, an entry is kept until it is deleted.
- `ListCollections()` lists every Collection in the database, read from the part of each Redis key before its first `:`. Keys written by other applications in the same database show up as Collections too, so give the Service its own `database`.

## Operations
| Method | Description |
|---|---|
| `ListCollections()` | Lists the names of the Collections that have Keys, sorted. Scans every key in the database |
| `ListKeys(collection, pattern, skip, take)` | Lists the Keys in a Collection that match `pattern`, sorted, then skips `skip` and returns at most `take`. `*` is the only wildcard; other characters (`?`, `[`, `]`, `\`) are matched literally |
| `Read(collection, keys)` | Gets the values of the Keys. A Key that isn't found (or has expired) is returned with a `null` value |
| `Count(collection)` | Gets the number of Keys in a Collection (`0` if it has none) |
| `Publish(collection, entries, ttl)` | Adds or replaces the entries. `ttl` (optional) sets when they expire |
| `Delete(collection, keys)` | Removes the Keys from a Collection. Keys that don't exist are ignored |

`ListCollections()`, `ListKeys()` and `Count()` use `SCAN` on every connected primary server, and read every matching key before sorting or counting. They are slower than `Read()` on a large database.

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Missing collection, pattern, keys or entries |
| `404` Not Found | `ListCollections()` or `ListKeys()` found nothing |
| `503` Not Available | `server` isn't set, or the server can't be reached |
| `504` Timeout | The server didn't answer in time (5 seconds) |
| `500` Internal Error | Any other error |

## Availability
`CheckAvailability()` connects (if it isn't connected yet) and sends a `PING`. The result isn't cached. Without `server`, it reports `Redis Connection Not Configured`.

## Tests
`tests/TrakHound.Redis.Service.Tests` checks that a server that can't be reached returns `503` without any setup, and runs the other tests against a Redis server when `TRAKHOUND_REDIS_TEST_SERVER` is set (with `TRAKHOUND_REDIS_TEST_PORT` and `TRAKHOUND_REDIS_TEST_PASSWORD` if needed). Each test uses its own Collections and deletes its Keys, so any database can be used. For example:

```bash
docker run -d --rm --name redis -p 6379:6379 redis:7
```
