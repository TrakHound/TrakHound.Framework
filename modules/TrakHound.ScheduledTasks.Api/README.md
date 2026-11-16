# TrakHound.ScheduledTasks.Api
Manage Scheduled Tasks that run Functions at a schedule using a CRON pattern.

This API module stores Scheduled Task configurations for a TrakHound Instance. Each configuration defines a **Function** to run, a **CRON schedule**, and optionally the **Job** (a named set of Parameters managed by `TrakHound.Functions.Api`) to run it with. Configurations are stored as JSON files in the API's Volume, where they are picked up by the `TrakHound.ScheduledTasks.Worker` which runs the Functions on schedule.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.ScheduledTasks.Api` |
| Category | `api` |
| Default Route | `scheduled-tasks/api` |
| Default Volume | `trakhound.scheduled-tasks` |
| Target Framework | `net8.0` |

## Storage
Configurations are stored in the API's Volume (default `trakhound.scheduled-tasks`) as individual JSON files named `{id}.json`. No additional TrakHound services (Tables, Blobs, etc.) are required.

The `TrakHound.ScheduledTasks.Worker` should be configured to use the **same Volume** so that it can read the configurations and respond to changes as they are published or deleted.

## Configuration
Scheduled Tasks are defined using the `ScheduledTaskConfiguration` model:

| Property | Type | Description |
|---|---|---|
| `id` | string | Unique identifier for the Scheduled Task. Also used as the configuration file name. **Required.** Letters, digits, `.`, `_`, and `-` (starting with a letter or digit), up to 100 characters |
| `enabled` | bool | Whether the Scheduled Task is active. Disabled tasks are not run by the Worker. |
| `description` | string | Description of the Scheduled Task |
| `functionId` | string | ID of the Function to run |
| `jobId` | string | ID of the Job (of the Function) whose Parameters the Function is run with. No Parameters when empty |
| `schedule` | string | CRON expression that defines when the Function is run |
| `timeZone` | string | Time Zone the Schedule is in (IANA or Windows ID). The server's local Time Zone when empty |
| `runMissed` | bool | Run once when the Worker starts if an occurrence was missed while it was stopped |
| `parameters` | object | **No longer used** (ignored by the Worker and dropped on Import) : Parameters come from the Job |

```json
{
  "id": "daily-report",
  "enabled": true,
  "description": "Generate the daily production report",
  "functionId": "Reports.DailyProduction",
  "jobId": "plant1",
  "schedule": "0 6 * * *",
  "timeZone": "America/New_York",
  "runMissed": true
}
```

### Schedule Format
The `schedule` property uses a standard CRON expression. Schedules are evaluated in the `timeZone` of the Task, or the **local time zone** of the host running the Worker when it's empty.

| Format | Fields | Example | Description |
|---|---|---|---|
| Standard | 5 | `*/15 * * * *` | Minute, Hour, Day of Month, Month, Day of Week |
| Include Seconds | 6 | `*/30 * * * * *` | Second, Minute, Hour, Day of Month, Month, Day of Week |

An expression with more than 5 fields is parsed as the **Include Seconds** format.

## Endpoints
All routes are relative to the API's route (default `scheduled-tasks/api`).

| Method | Route | Description |
|---|---|---|
| GET | *(root)* | List all Scheduled Task configurations (cached for 5 seconds: changes made through this Api show at once, Task Files edited directly in the Volume within 5 seconds) |
| GET | `{configurationId}` | Get a Scheduled Task configuration by ID |
| POST | *(root)* | Publish (create or replace) a Scheduled Task configuration. The request body is the configuration JSON. |
| DELETE | `{configurationId}` | Delete a Scheduled Task configuration by ID |
| GET | `export` | Export the Scheduled Tasks as a Document that can be imported on another Instance. Optional `ids` and `functionId` filters (comma separated) |
| POST | `import` | Import the Tasks of an Export Document. Query : `mode` (`fail`, `skip`, or `overwrite`) and `dryRun` |
| GET | `status` | The latest Status of each Worker |

### Examples
**List Configurations**
```bash
curl "http://localhost:8472/scheduled-tasks/api"
```

**Get Configuration**
```bash
curl "http://localhost:8472/scheduled-tasks/api/daily-report"
```

**Publish Configuration**
```bash
curl -X POST "http://localhost:8472/scheduled-tasks/api" \
     -H "Content-Type: application/json" \
     -d '{"id":"daily-report","enabled":true,"functionId":"Reports.DailyProduction","jobId":"plant1","schedule":"0 6 * * *"}'
```

**Delete Configuration**
```bash
curl -X DELETE "http://localhost:8472/scheduled-tasks/api/daily-report"
```

## Export / Import
Copy Scheduled Tasks to another Instance, or write them offline and import them.

> To copy Tasks that run a Job, use the **Schedules** Export / Import of `TrakHound.Functions.Api` (`functions/schedules/export` and `functions/schedules/import`, also on the Schedule Board and each Function's Schedule tab) : its Document also has the Jobs, and its Import checks that every Task's Job is on the Instance or in the Import. This Api doesn't know about Jobs.

**Export Document**
```json
{
  "format": "trakhound.scheduled-tasks",
  "version": 1,
  "exported": "2026-10-19T12:00:00Z",
  "sourceInstanceId": "...",
  "tasks": [
    { "id": "daily-report", "enabled": true, "functionId": "Reports.DailyProduction", "jobId": "plant1", "schedule": "0 6 * * *", "timeZone": "America/New_York" }
  ]
}
```
`format` and `version` can be left out of a Document written offline (`format` must match when it's set). `exported` and `sourceInstanceId` are informational.

**Import**
Every Task is validated before anything is written (ID, Function ID, Schedule, Time Zone, and IDs used more than once). Nothing is written when a Task is invalid, or when a different Task with the same ID exists and `mode` is `fail`.

| Mode | When a different Task with the same ID exists |
|---|---|
| `fail` *(default)* | Nothing is imported (the Task is a `conflict`) |
| `skip` | The existing Task is kept |
| `overwrite` | The existing Task is replaced |

A Task that's the same as the existing one is `unchanged` and isn't written. `dryRun=true` returns what would happen without writing. The Response lists each Task with its `action` (`create`, `update`, `unchanged`, `skip`, `conflict`, `invalid`, or `error`) and `messages`. It's returned with 200 when it succeeded, 400 when something is invalid or conflicts, and 500 when a Task couldn't be written. Values that are `false` or `0` (including `success`) are left out of the JSON.

```bash
curl "http://localhost:8472/scheduled-tasks/api/export" -o scheduled-tasks.json
curl -X POST "http://localhost:8472/scheduled-tasks/api/import?mode=overwrite&dryRun=true" -H "Content-Type: application/json" --data-binary @scheduled-tasks.json
```

## Response Codes
| Code | Description |
|---|---|
| 200 | Success. GET and POST return the configuration(s) as JSON. |
| 400 | Missing (or unsafe) configuration ID or request body. Import : a Task is invalid or conflicts |
| 404 | Configuration not found (or no configurations exist when listing) |
| 500 | Error writing the configuration to the Volume |

## Related Modules
| Module | Description |
|---|---|
| `TrakHound.ScheduledTasks.Worker` | Reads configurations from the Volume and runs the configured Functions on schedule |
| `TrakHound.Functions.Api` | Uses this API (`scheduled-tasks/api`) to create, update, and delete Scheduled Tasks for Functions, manages Jobs, and exports / imports Jobs with their Tasks (`functions/schedules/export` and `functions/schedules/import`) |

## Development
The project can be run standalone for debugging using `TrakHound.Debug.AspNetCore`. `Program.cs` connects to a TrakHound Instance at `localhost:8472`, uses a local Volume located in the `volume` directory of the build output, maps the API endpoints and streams, and serves the Api Debugger at `/_debug`.

```bash
dotnet run --project TrakHound.ScheduledTasks.Api.csproj
```

## Publishing
Package information is defined in `trakhound.package.json`. Its version is the `version` prefix in that file plus the git height, and the TrakHound CLI publishes it to the Publish Target in the nearest `trakhound.package.publish.json` (see [Versions from Git](https://www.trakhound.com/docs/trakhound-framework/packages#package-versions)).
