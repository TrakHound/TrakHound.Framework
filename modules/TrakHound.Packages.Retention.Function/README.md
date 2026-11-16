# TrakHound.Packages.Retention.Function

A Function that deletes older versions of each package from the Instance's package server (`TrakHound.Packages.Api`), keeping the latest ones. Run it on a schedule on an Instance that serves packages, so the stored versions don't grow without limit.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Packages.Retention.Function` |
| Category | `function` |

## Parameters
| Parameter | Required | Default | Description |
|---|---|---|---|
| `keep` | No | `5` | How many of the latest versions of each package to keep. Older versions are deleted. Must be at least 1 (otherwise the Run returns 400) |

## What it does
The Function calls the Packages Api on the same Instance, at the route `packages`:

1. Reads the Organizations (`GET /packages/organizations`).
2. For each Organization, reads its packages (`GET /packages/{organization}/latest`).
3. For each package, reads its versions (`GET /packages/{organization}/{id}/versions`) and orders them newest first.
4. Deletes each version after the first `keep` (`DELETE /packages/{organization}?packageUuid={uuid}`), which removes its file, table rows and cache entry.

Every Organization and package is cleaned up: there's no parameter to limit it to one. Each deletion is logged with its result, and a failed deletion is logged as an Error without stopping the Run. Deleted versions can't be recovered.

**Prereleases:** versions are compared as plain version numbers, so a prerelease version (ex. `1.2.4-dev.20261114153000`) can't be ordered or deleted. If a package has more than `keep` versions and any of them is a prerelease, the Run fails when it reaches that package. Don't run the Function on a server that stores prereleases.

## Requirements
- `TrakHound.Packages.Api` configured on the same Instance with the route `packages`.
- If the Function is a Member of a Zone, the Zone's Rules must allow it to call that Api (Instance User Guide, section 2.7.1).

## Running it
Run it from the Admin interface (Functions, Run tab), or on a schedule with a Scheduled Task (`TrakHound.ScheduledTasks.Worker`), ex. once a day with a Job that sets `keep`. See https://www.trakhound.com/docs/trakhound-framework/functions.

## Related Modules
| Package | Description |
|---|---|
| `TrakHound.Packages.Api` | The package server the Function cleans up |
| `TrakHound.Packages.App` | Browse, upload, download and copy packages between package servers |
| `TrakHound.ScheduledTasks.Worker` | Runs Functions on a CRON schedule |

## Development
`Program.cs` runs the Function once against an Instance at `localhost:8472`, using a local `volume` folder, and prints the log and the response. It doesn't apply the `keep` default: pass the parameters as JSON in the third argument, ex.

```bash
dotnet run --project TrakHound.Packages.Retention.Function.csproj -- _ _ "{\"keep\":\"5\"}"
```

See https://www.trakhound.com/docs/trakhound-framework/debugging.
