# UserPackageId
Describe what the App shows and who uses it.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `app` |

## Pages
Routes are relative to the App's route.

| Route | Description |
|---|---|
| `/` | The home page |

## Access
Describe the Roles or Permissions a visitor needs, and the Apis the App reads.

## What it calls
List the Apis and Services the App calls through `Client`, so an administrator can allow them when the package is in a Zone.

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
