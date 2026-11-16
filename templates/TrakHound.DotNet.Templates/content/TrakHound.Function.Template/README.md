# UserPackageId
Describe what the Function does and when to run it.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `function` |

## Input Parameters
Administrators, and AI agents using the Instance's MCP Server, read this table to run the Function. Keep it the same as the `[FunctionParameter]` attributes.

| Parameter | Required | Default | Description |
|---|---|---|---|
| `name` | No | `World` | Who to greet |

## Output
Describe what a successful Run returns, and what each error Status Code means.

## What it calls
List the Apis and Services the Function calls through `Client`, so an administrator can allow them when the package is in a Zone.

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
