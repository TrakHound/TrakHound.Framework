# UserPackageId
Describe what the Api does and who calls it.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `api` |

## Endpoints
Routes are relative to the Api's route. Keep this table the same as the endpoint attributes.

| Method | Route | Description |
|---|---|---|
| `GET` | `/` | Returns the time |
| `GET` | `/{name}` | Returns the time and `name` |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| | | | Describe each configuration parameter, or remove this section |

## What it calls
List the Apis and Services the Api calls through `Client`, so an administrator can allow them when the package is in a Zone.

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
