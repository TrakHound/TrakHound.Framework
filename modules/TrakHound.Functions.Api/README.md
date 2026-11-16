# TrakHound.Functions.Api
Run Functions and keep a record of each Run, with its Input, Output, Log and what started it. The Api also manages Jobs (named sets of Parameters to run a Function with) and the Scheduled Tasks that run Functions on a CRON schedule.

Install it on any Instance that runs Functions. The Admin interface uses it for the Functions Summary, a Function's History, Jobs and Schedule tabs, and the Schedule Board. Without it, the Admin interface runs a Function directly and the Run isn't recorded.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Functions.Api` |
| Category | `api` |
| Default Route | `functions` |

Keep the default route: the Admin interface and the `TrakHound.ScheduledTasks.Worker` call this Api at `functions`.

## Configuration
The Api has no configuration parameters.

## Services
Runs, Logs, Jobs and Schedule Board columns are stored with the Instance's Table Service, using `ansi` Table Requests so they work on every Table Service that supports them (ex. `TrakHound.Sqlite.Service`, `TrakHound.SqlServer.Service` or `TrakHound.Postgres.Service`). The Tables are created in the `trakhound` Schema the first time they're used:

| Table | What it holds |
|---|---|
| `function_run` | Each Run: status code, start, end and duration |
| `function_run_trigger` | What started each Run (`scheduled`, `manual` or `api`) and its Scheduled Task |
| `function_run_detail` | The package version that ran, the Job and the CPU used |
| `function_run_input` | A Run's Input Parameters, after the Job's Parameters are applied |
| `function_run_output` | A Run's Output Parameters |
| `function_log` | A Run's log |
| `function_job` | Jobs |
| `function_task` | Which Scheduled Task belongs to which Function and Job |
| `function_schedule_column` | The Schedule Board's columns |

Every row has the Instance's ID, so more than one Instance can share a database.

Scheduled Tasks themselves are kept by the `TrakHound.ScheduledTasks.Api` package, which this Api calls at `scheduled-tasks/api`. The Scheduled Task endpoints and the Schedules Export / Import need it.

Runs are recorded when they complete. A Run's Input is saved in the database, except Parameters the Function describes as `Secret`, so don't pass other secrets as Parameters. Use `TrakHound.Functions.Retention.Function` to delete old Runs and Logs.

## Endpoints
All routes are relative to the Api's route (default `functions`). None of the endpoints are marked as MCP Tools (`[ApiMcpTool]`). The Instance's MCP Server has its own Function tools.

### Functions
| Method | Route | Description |
|---|---|---|
| GET | `list` | Lists the Functions on the Instance (ID, name, description, package, run count and average duration). Returns 404 when there are none |
| GET | `summary` | Summarizes the Runs of each Function (or only `functionId`) that started between `start` and `stop` (default the last 24 hours) in `bucketCount` time buckets (default 24): by trigger (scheduled, manual or api) and by Scheduled Task, with the next Run of each Schedule |
| GET | `{functionId}` | Gets a Function's details: name, description, package, run count, average duration and its package ReadMe |

### Runs
| Method | Route | Description |
|---|---|---|
| POST | `{functionId}/run` | Runs a Function and waits for it to complete. The body holds its Input Parameters (names and string values). With `jobId`, the Job's Parameters are used and the body's are applied on top. Returns the Run's status code, Output Parameters and log. Query: `runId`, `timestamp`, `taskId`, `trigger` (`manual`) and `jobId` |
| POST | `{functionId}/runs/{runId}/cancel` | Cancels a Run that is still running. Returns 404 when it isn't running |
| GET | `runs` | Lists the Runs of every Function that started between `start` and `stop`, with each Run's status, status code, start, end and duration. Sorted by start (`sortOrder` 1 = oldest first, the default, or -1 = newest first) and paged with `skip` and `take` (default 100, up to 10,000). Returns 404 when there are none |
| GET | `{functionId}/runs` | Lists the Runs of a Function, with the same query parameters as `runs` |
| GET | `{functionId}/runs/{runId}` | Gets a Run: its status, status code, start, end, duration, what started it (trigger and Scheduled Task), the package version that ran, its Job and the CPU it used |
| GET | `{functionId}/runs/{runId}/input` | Gets a completed Run's Input Parameters. Returns 404 when the Run had no input or isn't recorded |
| GET | `{functionId}/runs/{runId}/output` | Gets a completed Run's Output Parameters. Returns 404 when the Run had no output or isn't recorded |
| GET | `{functionId}/runs/{runId}/status` | Gets a completed Run's statuses: Started at its start, then Completed, Stopped or Error at its end. A Run that is still running returns 404 |
| Stream | `{functionId}/runs/{runId}/status` | Streams each status of a Run as it happens |
| GET | `{functionId}/runs/{runId}/log` | Gets a Run's log at `minimumLevel` or more severe: Critical, Error, Warning, Information, Debug or Trace (the default) |
| Stream | `{functionId}/runs/{runId}/log` | Streams a Run's log as it is written, at `minimumLevel` or more severe (default Trace) |

Streams are served over WebSocket, or Server-Sent Events with `Accept: text/event-stream`. To see every status and log line of a Run, subscribe to its Streams first, then start the Run with the same `runId`.

A Run is recorded as `scheduled` when `taskId` is set, as `manual` when `trigger=manual`, and as `api` otherwise. A Run's status is Completed for a status code from 200 to 399, Stopped for 499 (cancelled) and Error for anything else.

### Jobs
A Job is a named set of Input Parameters that Runs and Scheduled Tasks can use. Its ID is the one given when it's added, or is made from its name, so it is the same on every Instance.

| Method | Route | Description |
|---|---|---|
| GET | `{functionId}/jobs` | Lists the Jobs of a Function, sorted by name |
| GET | `{functionId}/jobs/{jobId}` | Gets a Job: its name, description and parameters |
| POST | `{functionId}/jobs` | Adds a Job. The body has the `name` (required and unique for the Function), `description`, `parameters` and optionally an `id` |
| POST | `{functionId}/jobs/{jobId}` | Updates a Job's name, description and parameters. Scheduled Tasks that use the Job get the new parameters on their next Run |
| DELETE | `{functionId}/jobs/{jobId}` | Deletes a Job. Refused while Scheduled Tasks use it |

### Scheduled Tasks
| Method | Route | Description |
|---|---|---|
| GET | `{functionId}/tasks` | Lists the Scheduled Tasks that run the Function (schedule, time zone, enabled, and Job). Returns 404 when there are none |
| POST | `{functionId}/tasks` | Adds a Scheduled Task. Query: `schedule` (a CRON expression with 5 fields, or 6 with seconds), `description`, `enabled` (default true), `timeZone` (IANA or Windows ID, the server's when empty), `runMissed` and `jobId` |
| PUT | `{functionId}/tasks/{taskConfigurationId}` | Changes a Scheduled Task's schedule, and its description when one is given |
| POST | `{functionId}/tasks/{taskConfigurationId}` | Edits a Scheduled Task: its description, schedule, time zone, runMissed and Job are replaced with the ones in the body (no Job runs the Function without parameters). Whether it is enabled is kept |
| PUT | `{functionId}/tasks/{taskConfigurationId}/enable` | Enables a Scheduled Task so it runs on its schedule |
| PUT | `{functionId}/tasks/{taskConfigurationId}/disable` | Disables a Scheduled Task (it is kept and can be enabled again) |
| DELETE | `{functionId}/tasks/{taskConfigurationId}` | Deletes a Scheduled Task |
| GET | `tasks/{taskId}/last-run` | Gets the start of a Scheduled Task's latest recorded Run (`lastRun` is null when it has no Runs) |

### Schedule Board
| Method | Route | Description |
|---|---|---|
| GET | `schedule-columns` | Lists the Schedule Board's columns: one per Schedule, with its title and interval |
| POST | `schedule-columns` | Adds a column for a Schedule (`schedule`, a CRON expression), or changes its `title` |
| DELETE | `schedule-columns` | Removes the column for `schedule`. Refused while Scheduled Tasks use its Schedule |

### Schedules Export / Import
Copy Jobs and Scheduled Tasks to another Instance as one document. The Admin interface has **Export** and **Import** on the Schedule Board and on a Function's Schedule tab.

| Method | Route | Description |
|---|---|---|
| GET | `schedules/export` | Exports the Jobs and Scheduled Tasks of every Function, or of the comma separated Function IDs in `functionId` |
| POST | `schedules/import` | Imports the Jobs and Scheduled Tasks of an export document (the body) |

Import validates everything first, and writes nothing if anything is invalid. When a Job or Task with the same ID exists, `mode=fail` (the default) refuses the import, `mode=skip` keeps the existing one and `mode=overwrite` replaces it. `dryRun=true` only reports what would happen.

### Examples
**Run a Function with a Job**
```bash
curl -X POST "http://localhost:8472/functions/Reports.DailyProduction/run?jobId=plant1" \
     -H "Content-Type: application/json" \
     -d '{"date":"2026-11-16"}'
```

**List the newest Runs of a Function**
```bash
curl "http://localhost:8472/functions/Reports.DailyProduction/runs?start=2026-11-15&stop=2026-11-17&sortOrder=-1&take=20"
```

**Copy the Schedules to another Instance**
```bash
curl "http://localhost:8472/functions/schedules/export" -o schedules.json
curl -X POST "http://other-instance:8472/functions/schedules/import?mode=overwrite&dryRun=true" -H "Content-Type: application/json" --data-binary @schedules.json
```

## Related Modules
| Module | Description |
|---|---|
| `TrakHound.ScheduledTasks.Api` | Stores the Scheduled Task configurations (this Api calls it at `scheduled-tasks/api`) |
| `TrakHound.ScheduledTasks.Worker` | Runs the Scheduled Tasks through this Api's `{functionId}/run` route, so scheduled Runs are recorded |
| `TrakHound.Functions.Retention.Function` | Deletes Runs and Logs older than a number of days |

## Development
`Program.cs` runs the Api standalone with `TrakHound.Debug.AspNetCore`. It connects to a TrakHound Instance at `localhost:8472`, uses a local Volume in the `volume` folder of the build output, maps the Api's endpoints and streams, and serves the Api Debugger at `/_debug`.

```bash
dotnet run --project TrakHound.Functions.Api.csproj
```

See https://www.trakhound.com/docs/trakhound-framework/functions, https://www.trakhound.com/docs/trakhound-framework/apis and https://www.trakhound.com/docs/trakhound-framework/debugging.
