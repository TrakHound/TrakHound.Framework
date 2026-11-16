# UserPackageId
Describe how the Identity Provider signs users or clients in, and when to use it.

## Package Information
| Property | Value |
|---|---|
| Id | `UserPackageId` |
| Category | `identity` |

## How it signs in
Requests send a token in the `X-Example-Token` header. Describe how the token is checked, and the User ID and Roles it signs in with. Grant those Roles permissions with a Security Profile that uses this provider.

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| | | | Describe each configuration parameter, or remove this section |

## Security
Describe how secrets are stored and anything an administrator should know before using it.

## Development
`Program.cs` runs the package with the debug host, which connects to the Instance in `trakhound.debug.json`. See https://www.trakhound.com/docs/trakhound-framework/debugging.
