# TrakHound.ScheduledTasks.Worker
Runs Functions on the CRON schedules of the Scheduled Tasks, with the Parameters of each Task's Job, and reports the status of each Task.

The Worker reads the Scheduled Task configurations that `TrakHound.ScheduledTasks.Api` stores in its Volume, and runs each enabled Task's Function through `TrakHound.Functions.Api`, which records the Run and the Task that started it. Install all three packages to schedule Functions. The Tasks are managed on a Function's Schedule tab and on the Schedule Board in the Admin interface.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.ScheduledTasks.Worker` |
| Category | `worker` |
| Default Volume | `trakhound.scheduled-tasks` |

## Configuration
| Parameter | Required | Default | Description |
|---|---|---|---|
| `maxConcurrentRuns` | No | Half the processor count (at least 2) | How many Runs can be in progress at a time across all Tasks. Runs execute in the Instance's process |
| `overlapPolicy` | No | `skip` | What happens when a Task is due while its previous Run is still in progress: `skip`, `queue` or `concurrent` (see below) |
| `maxConcurrentRunsPerTask` | No | `2` | How many Runs of one Task can be in progress at a time. Only used with the `concurrent` Overlap Policy, and never more than `maxConcurrentRuns` |

A value that isn't a number above 0 (or isn't one of the Overlap Policies) uses the default.

### Overlap Policy
| Policy | When a Task is due while its previous Run is in progress |
|---|---|
| `skip` *(default)* | The occurrence is skipped (shown as Missed in the Functions Summary) |
| `queue` | One more Run starts after the current Run finishes. Further occurrences are skipped |
| `concurrent` | The Run starts alongside the current Run, up to `maxConcurrentRunsPerTask` |

A Run that is waiting for a free slot and hasn't started by the Task's next occurrence is dropped.

## Volume
The Worker reads every `{id}.json` file in its Volume as a Scheduled Task, and watches the Volume so a Task that is added, changed, disabled or deleted is started or stopped at once. Configure it with the **same Volume** as `TrakHound.ScheduledTasks.Api` (both default to `trakhound.scheduled-tasks`).

A Task runs when it is `enabled` and has a `functionId` and a `schedule`. See the `TrakHound.ScheduledTasks.Api` ReadMe for the configuration format. A Task with an invalid Schedule or Time Zone isn't run, and its error is shown in the Worker's status. A Task's old `parameters` are ignored (a warning is logged): Parameters come from its Job.

## Schedules
- The `schedule` is a CRON expression with 5 fields (minute, hour, day of month, month, day of week), or 6 with seconds first.
- It is evaluated in the Task's `timeZone` (an IANA or Windows ID), or in the server's local time zone when it's empty.
- When the Worker wakes late (ex. the machine was asleep), occurrences that already passed are skipped instead of run back to back.

### Missed Runs
When a Task has `runMissed` set, the Worker checks its last Run when the Worker starts (not when the Task is changed later). If an occurrence was missed since that Run, and it is at least 1 minute old, the Task runs once. The last Run comes from `TrakHound.Functions.Api` (`functions/tasks/{taskId}/last-run`), and Runs older than 7 days are ignored, so a Task that hasn't run in 7 days or more isn't caught up. The check is tried 3 times, 10 seconds apart, while the Api starts.

## Running a Function
For each due occurrence, the Worker posts to `functions/{functionId}/run` with the Task's ID (`taskId`) and Job (`jobId`). The Run gets the Job's Parameters as they are when it starts. A Task without a Job runs the Function with no Parameters.

When the Worker stops, it cancels the Runs in progress and waits for them to end.

## Status
The Worker posts its status to `scheduled-tasks/api/status` every 2 seconds when something changed, and at least every 10 seconds. The status has the Worker's settings, the Runs in progress and waiting, each Task's state (`scheduled`, `waiting`, `running`, `ended` or `invalid`) with its next Run and counts since the Worker started, the upcoming occurrences, and the recent events. The Schedule Board shows it. Read it with `GET scheduled-tasks/api/status`.

## Services
The Worker uses no Services directly. It calls these Apis at their default routes:

| Route | Package | Used for |
|---|---|---|
| `functions` | `TrakHound.Functions.Api` | Running the Functions, and the last Run of a Task for `runMissed` |
| `scheduled-tasks/api` | `TrakHound.ScheduledTasks.Api` | Posting the Worker's status |

## Related Modules
| Module | Description |
|---|---|
| `TrakHound.ScheduledTasks.Api` | Stores the Scheduled Task configurations in the Volume this Worker reads, and serves its status |
| `TrakHound.Functions.Api` | Runs and records the Functions, and manages the Jobs and Scheduled Tasks of each Function |

## Development
`Program.cs` runs the Worker standalone against a TrakHound Instance at `localhost:8472`, with a local Volume in the `volume` folder of the build output (put Task files there), and prints its log until a key is pressed. It uses the default configuration parameters.

```bash
dotnet run --project TrakHound.ScheduledTasks.Worker.csproj
```

See https://www.trakhound.com/docs/trakhound-framework/functions and https://www.trakhound.com/docs/trakhound-framework/debugging.
