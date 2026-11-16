# TrakHound.Http.Service
HTTP Services for the TrakHound Framework to route Services to another TrakHound Instance.

This service module forwards every Service request to the HTTP Services API (`/_services`) of a remote TrakHound Instance. The remote Instance then routes the request to its own Services (ex. Redis, MQTT, RabbitMQ, SQL Server) using its Service Routers, and for Table Requests the remote Service generates or selects the SQL for its database. This allows an Instance to use Services that are only configured (or only reachable) on another Instance.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Http.Service` |
| Category | `service` |
| Target Framework | `net8.0` |

## Services
| Class | Service Type | Remote Endpoint |
|---|---|---|
| `HttpBlobService` | Blobs (`ITrakHoundBlobService`) | `_services/blobs` |
| `HttpKeyValueService` | Key-Values (`ITrakHoundKeyValueService`) | `_services/key-values` |
| `HttpMessageService` | Messages (`ITrakHoundMessageService`) | `_services/messages` |
| `HttpMessageQueueService` | Message Queues (`ITrakHoundMessageQueueService`) | `_services/message-queues` |
| `HttpTableService` | Tables (`ITrakHoundTableService`) | `_services/tables` |
| `HttpTimeSeriesService` | Time Series (`ITrakHoundTimeSeriesService`) | `_services/time-series` |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `hostname` | Yes | | Hostname of the remote TrakHound Instance |
| `port` | No | `8472` | HTTP port of the remote Instance |
| `path` | No | `/` | Base path of the remote Instance |
| `useSsl` | No | `false` | Use HTTPS (and WSS for Subscriptions) |
| `routerId` | No | | Service Router on the remote Instance to route requests to. If not set, the remote Instance's default Router is used |
| `apiKey` | No | | An API Key created on the remote Instance (Security > API Keys), sent with every request. Saved encrypted |

If `hostname` is not set, every request returns `503` (Not Available) with the message `Remote Instance Not Configured (hostname)`.

## Remote Instance Requirements
- The System API must be enabled on the remote Instance (`/_services` returns `404` otherwise).
- The remote Instance only allows Service operations from another machine when an Identity Profile covers them (otherwise it returns `403`). Protect them with a Profile for the API Keys provider and set `apiKey`, or allow Unprotected Remote Access on the remote Instance (Settings > Interfaces) on a trusted network.
- The remote Instance must have Services (and a Router) configured for each Service type used.
- Do not point the service at the same Instance (or at an Instance that routes back to this one). Requests would be forwarded in a loop.

## Availability
`CheckAvailability()` calls the remote `availability` endpoint for the Service type and returns the remote Service's availability. If the remote Instance can't be reached, or returns an error, the Service is reported as unavailable with the remote URL and status code in the message.

## Response Codes
Status codes and messages from the remote Instance are returned as is. If the remote Instance can't be reached, `503` (Not Available) is returned with the message `Remote Instance ({url}) Unreachable : {error}`.

## Subscriptions
`Subscribe()` (Messages and Message Queues) returns a stream backed by a WebSocket connection to the remote Instance (`ws://` or `wss://` depending on `useSsl`). Disposing the stream closes the connection.

## Limitations
| Operation | Limitation |
|---|---|
| Key-Values `Publish()` | The `ttl` parameter is not supported by the HTTP API and is ignored (the remote Service uses its default) |
| Blobs `Upload()` | `metadata` is not supported by the HTTP API and is ignored. If `contentType` is empty, `application/octet-stream` is used |
| Message Queues `Acknowledge()` / `Reject()` | Delivery IDs are sent to the remote Instance in a separate request, so they only work if the remote Service accepts them outside of the original Pull / Subscription |

## Publishing
Package information is defined in `trakhound.package.json`. Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see [Versions from Git](https://www.trakhound.com/docs/trakhound-framework/packages#package-versions)).
