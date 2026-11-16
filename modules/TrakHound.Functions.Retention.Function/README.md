# TrakHound.Functions.Retention.Function
Deletes Function Runs and their Logs that are older than a number of days. The `TrakHound.Functions.Api` package records every Run in the Instance's Table Service and never deletes them, so run this Function on a schedule (ex. once a day with a Scheduled Task) to keep the database from growing.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Functions.Retention.Function` |
| Category | `function` |

## Parameters
| Parameter | Required | Default | Description |
|---|---|---|---|
| `days` | No | `30` | How many days of Function Runs and Logs to keep (older ones are deleted). An Integer, at least 1 |

The cutoff is midnight (the server's local time) `days` days ago. A `days` value below 1 returns 400 and deletes nothing.

## What it deletes
The Function deletes rows from these Tables in the `trakhound` Schema, which `TrakHound.Functions.Api` creates:

| Table | Rows deleted |
|---|---|
| `function_run_output` | Output Parameters of Runs that started before the cutoff |
| `function_run_input` | Input Parameters of Runs that started before the cutoff |
| `function_run_detail` | Run details that started before the cutoff |
| `function_run_trigger` | Run triggers that started before the cutoff |
| `function_run` | Runs that started before the cutoff |
| `function_log` | Log lines written before the cutoff |

Jobs, Scheduled Tasks and Schedule Board columns are not deleted. Tables that don't exist yet are skipped.

Rows of every Instance in the database are deleted, not only the Instance that runs the Function. When Instances share a database, run it on one of them.

Rows are deleted in batches of 5000 without a transaction, so each batch commits on its own and locks are held briefly. The command can run for up to an hour, so a large backlog can be deleted in one Run.

## Services
The Function uses the Instance's Table Service (`ansi` Table Requests). It needs a Table Service that supports them, ex. `TrakHound.Sqlite.Service`, `TrakHound.SqlServer.Service` or `TrakHound.Postgres.Service`.

## Related Modules
| Module | Description |
|---|---|
| `TrakHound.Functions.Api` | Records the Runs and Logs that this Function deletes |
| `TrakHound.ScheduledTasks.Worker` | Runs this Function on a schedule. Add the Scheduled Task (and a Job to set `days`) on the Function's Schedule tab in the Admin interface |

## Development
`Program.cs` runs the Function once against a TrakHound Instance at `localhost:8472`, with a local Volume in the `volume` folder of the build output, and prints its log and response. The Input Parameters are read as JSON from the third command line argument. Pass `days`, since this `Program.cs` doesn't apply the Parameter's default.

See https://www.trakhound.com/docs/trakhound-framework/functions and https://www.trakhound.com/docs/trakhound-framework/debugging.
