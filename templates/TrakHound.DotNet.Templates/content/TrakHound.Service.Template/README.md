# UserPackageId
Describe what the Service stores or connects to, and when to use it.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `service` |

## Services
| Class | Service Type |
|---|---|
| `Service` | Key-Values (`ITrakHoundKeyValueService`) |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| | | | Describe each configuration parameter (ex. the server and credentials), or remove this section |

## Errors
Errors are returned with a Status Code that tells a Service Router whether to try the next Target.

| Status Code | Errors |
|---|---|
| `400` Bad Request | Missing or invalid parameters |
| `503` Not Available | The Service isn't configured, or can't reach what it connects to |
| `500` Internal Error | Any other error |

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
