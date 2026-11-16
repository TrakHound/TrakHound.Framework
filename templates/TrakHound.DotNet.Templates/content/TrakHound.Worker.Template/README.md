# UserPackageId
Describe what the Worker does while it runs.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `worker` |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `interval` | No | `5000` | How often the Worker runs, in milliseconds |

## What it calls
List the Apis and Services the Worker calls through `Client`, so an administrator can allow them when the package is in a Zone.

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
