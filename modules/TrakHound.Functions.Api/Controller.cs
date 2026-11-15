// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Logging;
using TrakHound.ScheduledTasks;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Functions
{
    public class Controller : TrakHoundApiController
    {
        private const string _defaultScheduledTasksRoute = "scheduled-tasks/api";
        private const int _cancelledStatusCode = 499;
        private const int _maxRunsTake = 10000;


        private static string GetRunStatus(int statusCode)
        {
            if (statusCode >= 200 && statusCode < 400) return TrakHoundFunctionStatusType.Completed.ToString();
            if (statusCode == _cancelledStatusCode) return TrakHoundFunctionStatusType.Stopped.ToString();
            return TrakHoundFunctionStatusType.Error.ToString();
        }

        /// <summary>
        /// Orders Runs by their Start (oldest first, or newest first for a Descending sortOrder)
        /// </summary>
        private static string GetRunsOrder(int sortOrder)
        {
            var direction = sortOrder == (int)TrakHoundSortOrder.Descending ? "desc" : "asc";
            return $" order by \"start\" {direction}, \"id\"";
        }

        /// <summary>
        /// CPU usage over a Run as a percent of total machine capacity (0 - 100)
        /// </summary>
        private static double GetCpuUsage(double cpuTime, TimeSpan duration)
        {
            if (cpuTime <= 0 || duration.TotalMilliseconds <= 0) return 0;
            return Math.Round(Math.Clamp(cpuTime / (duration.TotalMilliseconds * Environment.ProcessorCount) * 100, 0, 100), 4);
        }

        private static string GetRunTrigger(string taskId, string trigger)
        {
            if (!string.IsNullOrEmpty(taskId)) return FunctionRunTriggerModel.Scheduled;
            if (string.Equals(trigger, FunctionRunTriggerModel.Manual, StringComparison.OrdinalIgnoreCase)) return FunctionRunTriggerModel.Manual;
            return FunctionRunTriggerModel.Api;
        }

        /// <summary>
        /// The names of the Parameters the Function describes as Secret (not saved with a Run's Input)
        /// </summary>
        private async Task<HashSet<string>> GetSecretParameters(string functionId)
        {
            var secrets = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                var information = await Client.Functions.GetInformation(functionId);
                if (information?.Parameters != null)
                {
                    foreach (var parameter in information.Parameters.Where(o => o.Secret)) secrets.Add(parameter.Name);
                }
            }
            catch { }

            return secrets;
        }

        #region "Tables"

        /// <summary>
        /// A Table that this Api creates (if it doesn't exist) before it is first used
        /// </summary>
        private sealed class FunctionTable
        {
            public string Name { get; init; }
            public Action<TrakHoundTableRequestBuilder> AppendCreate { get; init; }

            public static FunctionTable Create<TModel>() => new FunctionTable { Name = typeof(TModel).FullName, AppendCreate = o => o.AppendEnsureTable<TModel>() };
        }

        private static readonly FunctionTable _runTable = FunctionTable.Create<FunctionRunModel>();
        private static readonly FunctionTable _runTriggerTable = FunctionTable.Create<FunctionRunTriggerModel>();
        private static readonly FunctionTable _runOutputTable = FunctionTable.Create<FunctionRunOutputModel>();
        private static readonly FunctionTable _runInputTable = FunctionTable.Create<FunctionRunInputModel>();
        private static readonly FunctionTable _runDetailTable = FunctionTable.Create<FunctionRunDetailModel>();
        private static readonly FunctionTable _logTable = FunctionTable.Create<FunctionLogModel>();
        private static readonly FunctionTable _jobTable = FunctionTable.Create<FunctionJobModel>();
        private static readonly FunctionTable _taskTable = FunctionTable.Create<FunctionTaskModel>();
        private static readonly FunctionTable _scheduleColumnTable = FunctionTable.Create<FunctionScheduleColumnModel>();

        // The Tables this process created (or found), by Instance and Table, so they aren't created again on every Request
        private static readonly ConcurrentDictionary<string, bool> _createdTables = new ConcurrentDictionary<string, bool>();

        private string GetTableKey(FunctionTable table) => $"{InstanceId}:{table.Name}";

        /// <summary>
        /// Creates the Tables that this process hasn't created yet (so reads before the first write return no rows instead of an error)
        /// </summary>
        private async Task<TrakHoundServiceResponse> CreateTables(CancellationToken cancellationToken, params FunctionTable[] tables)
        {
            var missing = tables.Where(o => !_createdTables.ContainsKey(GetTableKey(o))).ToList();
            if (missing.Count == 0) return TrakHoundServiceResponse.Ok();

            var createBuilder = new TrakHoundTableRequestBuilder();
            foreach (var table in missing) table.AppendCreate(createBuilder);

            var createResponse = await Client.Services.Tables.ExecuteNonQuery(createBuilder.Build(), cancellationToken: cancellationToken);
            if (createResponse.Success)
            {
                foreach (var table in missing) _createdTables[GetTableKey(table)] = true;
            }

            return createResponse;
        }

        // A Table was dropped (or the database changed) after this process created it. SQL Server : 208, PostgreSQL : 42P01, SQLite : "no such table"
        private static bool IsTableNotFound(int statusCode, string message)
        {
            return statusCode == 400 && message != null && (message.Contains("SqlException : 208 :") || message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || message.Contains(": 42P01 :") || message.Contains("no such table", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Runs a Query after creating its Tables. When it fails because a Table doesn't exist, the Tables are created again and it is run once more
        /// </summary>
        private async Task<TrakHoundServiceResponse<TResult>> RunWithTables<TResult>(Func<Task<TrakHoundServiceResponse<TResult>>> run, CancellationToken cancellationToken, params FunctionTable[] tables)
        {
            var createResponse = await CreateTables(cancellationToken, tables);
            if (!createResponse.Success) return new TrakHoundServiceResponse<TResult>(default, createResponse.StatusCode, createResponse.Message);

            var response = await run();
            if (IsTableNotFound(response.StatusCode, response.Message))
            {
                foreach (var table in tables) _createdTables.TryRemove(GetTableKey(table), out _);

                createResponse = await CreateTables(cancellationToken, tables);
                if (!createResponse.Success) return new TrakHoundServiceResponse<TResult>(default, createResponse.StatusCode, createResponse.Message);

                response = await run();
            }

            return response;
        }

        /// <summary>
        /// Runs a Query that returns no results after creating its Tables (see RunWithTables)
        /// </summary>
        private async Task<TrakHoundServiceResponse> ExecuteWithTables(Func<Task<TrakHoundServiceResponse>> run, CancellationToken cancellationToken, params FunctionTable[] tables)
        {
            var response = await RunWithTables(async () =>
            {
                var executeResponse = await run();
                return new TrakHoundServiceResponse<bool>(executeResponse.Success, executeResponse.StatusCode, executeResponse.Message);
            }, cancellationToken, tables);

            return new TrakHoundServiceResponse(response.StatusCode, response.Message);
        }

        #endregion


        [ApiGet("list", Description = "Lists the Functions on the Instance (ID, name, description, package, run count and average duration). Returns 404 when there are none.")]
        public async Task<TrakHoundApiResponse> GetFunctions()
        {
            var informations = await Client.Functions.GetInformation();
            if (!informations.IsNullOrEmpty())
            {
                return Ok(informations);
            }
            else
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Runs of each Function (or just functionId) between start and stop (default the last 24 hours),
        /// by Trigger and by Scheduled Task, with the next Run of each Schedule
        /// </summary>
        [ApiGet("summary", Description = "Summarizes the Runs of each Function (or only functionId) that started between start and stop (default the last 24 hours) in bucketCount time buckets: by trigger (scheduled, manual or api) and by Scheduled Task, with the next Run of each Schedule.")]
        public async Task<TrakHoundApiResponse> GetSummary(
            [FromQuery(Description = "The earliest Run start to include, as a date and time (default 24 hours ago)")] string start = null,
            [FromQuery(Description = "Runs that start before this are included, as a date and time (default now)")] string stop = null,
            [FromQuery(Description = "Only summarize this Function (every Function when empty)")] string functionId = null,
            [FromQuery(Description = "How many time buckets to split the range into")] int bucketCount = 24,
            CancellationToken cancellationToken = default
            )
        {
            if (string.IsNullOrEmpty(InstanceId)) return BadRequest();

            var now = DateTime.UtcNow;
            var stopTime = !string.IsNullOrEmpty(stop) ? stop.ToDateTime() : now;
            var startTime = !string.IsNullOrEmpty(start) ? start.ToDateTime() : stopTime.AddDays(-1);
            if (stopTime <= startTime) return BadRequest();

            bucketCount = Math.Clamp(bucketCount, 1, 500);
            var bucketSize = TimeSpan.FromTicks((stopTime - startTime).Ticks / bucketCount);

            // The end of the range that Schedule occurrences are compared to Runs in (see SetTaskSchedule)
            var scheduleStop = DateTime.SpecifyKind(stopTime < now ? stopTime : now, DateTimeKind.Utc) - _missedGracePeriod;

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("start", startTime.ToUnixTime());
            parameters.Add("stop", stopTime.ToUnixTime());
            parameters.Add("bucketStart", DateTime.SpecifyKind(startTime, DateTimeKind.Utc).ToUnixTime()); // The Timeline Timestamps' base (startTime's ticks, as UTC)
            parameters.Add("bucketSize", bucketSize.Ticks * 100); // Nanoseconds, like the [start] column
            parameters.Add("scheduleStop", (scheduleStop + _scheduleTolerance).ToUnixTime());
            parameters.Add("cancelledStatusCode", _cancelledStatusCode);
            parameters.Add("scheduled", FunctionRunTriggerModel.Scheduled);
            parameters.Add("manual", FunctionRunTriggerModel.Manual);
            parameters.Add("api", FunctionRunTriggerModel.Api);

            var conditions = new List<string>();
            conditions.Add("\"run\".\"instance_id\" = @instanceId");
            conditions.Add("\"run\".\"start\" >= @start");
            conditions.Add("\"run\".\"start\" < @stop");

            if (!string.IsNullOrEmpty(functionId))
            {
                parameters.Add("functionId", functionId);
                conditions.Add("\"run\".\"function_id\" = @functionId");
            }

            var query = GetSummaryQuery(string.Join(" and ", conditions));

            var summaryResponse = await RunWithTables(() => Client.Services.Tables.ReadMultiple<FunctionSummaryRow>(TrakHoundTableRequest.Query(query, parameters), cancellationToken: cancellationToken), cancellationToken, _runTable, _runTriggerTable);
            if (!summaryResponse.Success && summaryResponse.StatusCode != 404) return InternalError(summaryResponse.Message); // 404 = No Runs

            // One pass over the rows : the Runs of each Function, of each Function's Scheduled Tasks, and of each Function in each time bucket
            var functionRows = new Dictionary<string, FunctionSummaryRow>(StringComparer.OrdinalIgnoreCase);
            var taskRows = new Dictionary<string, FunctionSummaryRow>(StringComparer.OrdinalIgnoreCase);
            var bucketRows = new Dictionary<string, List<FunctionSummaryRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in summaryResponse.Result ?? Enumerable.Empty<FunctionSummaryRow>())
            {
                if (string.IsNullOrEmpty(row?.FunctionId)) continue;

                switch (row.Grouping)
                {
                    case FunctionSummaryRow.FunctionGrouping:
                        functionRows[row.FunctionId] = row;
                        break;

                    case FunctionSummaryRow.TaskGrouping:
                        if (!string.IsNullOrEmpty(row.TaskId)) taskRows[GetTaskKey(row.FunctionId, row.TaskId)] = row; // Runs without a Task aren't listed
                        break;

                    case FunctionSummaryRow.BucketGrouping:
                        if (row.Bucket >= 0 && row.Bucket < bucketCount)
                        {
                            if (!bucketRows.TryGetValue(row.FunctionId, out var functionBuckets))
                            {
                                functionBuckets = new List<FunctionSummaryRow>();
                                bucketRows[row.FunctionId] = functionBuckets;
                            }
                            functionBuckets.Add(row);
                        }
                        break;
                }
            }

            // Scheduled Tasks (none when the TrakHound.ScheduledTasks.Api package isn't installed)
            IEnumerable<FunctionTask> tasks = null;
            try { tasks = await Client.Api.GetJson<IEnumerable<FunctionTask>>(_defaultScheduledTasksRoute, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { Log(TrakHoundLogLevel.Debug, $"Error Reading Scheduled Tasks : {ex.Message}"); }

            var tasksByFunction = (tasks ?? Enumerable.Empty<FunctionTask>())
                .Where(o => !string.IsNullOrEmpty(o?.Id) && !string.IsNullOrEmpty(o.FunctionId))
                .ToLookup(o => o.FunctionId, StringComparer.OrdinalIgnoreCase);

            // Jobs (named Parameters) by Function
            var jobsResponse = await ReadJobs(functionId, cancellationToken);
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);
            var jobsByFunction = jobsResponse.Jobs.ToLookup(o => o.FunctionId, StringComparer.OrdinalIgnoreCase);

            // Configured Functions, plus Functions with Runs (or Tasks) that are no longer configured
            var informations = await Client.Functions.GetInformation();
            var functionIds = new List<string>();
            if (!informations.IsNullOrEmpty()) functionIds.AddRange(informations.Select(o => o.FunctionId));
            functionIds.AddRange(functionRows.Keys);
            functionIds.AddRange(tasksByFunction.Select(o => o.Key));
            functionIds = functionIds
                .Where(o => !string.IsNullOrEmpty(o))
                .Where(o => string.IsNullOrEmpty(functionId) || string.Equals(o, functionId, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var summaries = new List<FunctionSummary>();
            foreach (var id in functionIds)
            {
                var information = informations?.FirstOrDefault(o => string.Equals(o.FunctionId, id, StringComparison.OrdinalIgnoreCase));
                var functionRow = functionRows.GetValueOrDefault(id);

                var summary = new FunctionSummary();
                summary.FunctionId = information?.FunctionId ?? id;
                summary.PackageId = information?.PackageId;
                summary.PackageVersion = information?.PackageVersion;
                summary.Jobs = jobsByFunction[id].OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                summary.SetRuns(functionRow, functionRow?.FunctionLastRun, functionRow?.FunctionLastRunId, functionRow?.FunctionLastStatusCode, GetRunStatus);
                summary.ScheduledRunCount = functionRow?.ScheduledCount ?? 0;
                summary.ManualRunCount = functionRow?.ManualCount ?? 0;
                summary.ApiRunCount = functionRow?.ApiCount ?? 0;

                // Runs per time bucket
                summary.Timeline = new List<FunctionTimelineBucket>();
                for (var i = 0; i < bucketCount; i++)
                {
                    summary.Timeline.Add(new FunctionTimelineBucket { Timestamp = DateTime.SpecifyKind(startTime + (bucketSize * i), DateTimeKind.Utc) });
                }
                foreach (var bucketRow in bucketRows.GetValueOrDefault(id) ?? Enumerable.Empty<FunctionSummaryRow>())
                {
                    var bucket = summary.Timeline[(int)bucketRow.Bucket.Value];
                    bucket.RunCount += bucketRow.RunCount;
                    bucket.ErrorCount += bucketRow.RunCount - bucketRow.SuccessCount - bucketRow.CancelledCount;
                }

                // Scheduled Tasks
                summary.Tasks = new List<FunctionTaskSummary>();
                foreach (var task in tasksByFunction[id].OrderBy(o => o.Description ?? o.Id))
                {
                    var taskRow = taskRows.GetValueOrDefault(GetTaskKey(id, task.Id));

                    var taskSummary = new FunctionTaskSummary();
                    taskSummary.Id = task.Id;
                    taskSummary.Enabled = task.Enabled;
                    taskSummary.Description = task.Description;
                    taskSummary.Schedule = task.Schedule;
                    taskSummary.TimeZone = task.TimeZone;
                    taskSummary.JobId = task.JobId;
                    taskSummary.JobName = !string.IsNullOrEmpty(task.JobId) ? summary.Jobs.FirstOrDefault(o => o.Id == task.JobId)?.Name : null;
                    taskSummary.RunMissed = task.RunMissed;
                    taskSummary.Parameters = task.Parameters;
                    taskSummary.SetRuns(taskRow, taskRow?.TaskLastRun, taskRow?.TaskLastRunId, taskRow?.TaskLastStatusCode, GetRunStatus);
                    SetTaskSchedule(taskSummary, taskRow?.FirstStart, taskRow?.ScheduleRunCount ?? 0, now, scheduleStop);

                    summary.Tasks.Add(taskSummary);
                }

                summary.NextRun = summary.Tasks.Where(o => o.NextRun != null).Min(o => o.NextRun);

                summaries.Add(summary);
            }

            return Ok(summaries);
        }

        /// <summary>
        /// Aggregates the Runs that match where (with their Triggers), grouped three ways (see FunctionSummaryRow.Grouping): by Function,
        /// by Function and Scheduled Task, and by Function and time bucket. SQL Server does it in one pass with grouping sets,
        /// which not every database has (ansi repeats the aggregates for each grouping)
        /// </summary>
        private static TrakHoundTableQueryVariants GetSummaryQuery(string where)
        {
            var runTable = $"\"{FunctionRunModel.SchemaName}\".\"{FunctionRunModel.TableName}\"";
            var triggerTable = $"\"{FunctionRunTriggerModel.SchemaName}\".\"{FunctionRunTriggerModel.TableName}\"";

            // Bucket = the same truncating division as the Timeline index ((start - bucketStart) / bucketSize)
            var runs = $@"with ""runs"" as
(
    select
        ""run"".""function_id"",
        ""run"".""id"",
        ""run"".""status_code"",
        ""run"".""start"",
        coalesce(""run"".""duration"", 0) as ""duration"",
        ""run_trigger"".""trigger"",
        ""run_trigger"".""task_id"",
        (""run"".""start"" - @bucketStart) / nullif(@bucketSize, 0) as ""bucket"",
        row_number() over (partition by ""run"".""function_id"" order by ""run"".""start"" desc) as ""function_rank"",
        row_number() over (partition by ""run"".""function_id"", ""run_trigger"".""task_id"" order by ""run"".""start"" desc) as ""task_rank""
    from {runTable} as ""run""
    left join {triggerTable} as ""run_trigger""
        on ""run_trigger"".""instance_id"" = ""run"".""instance_id""
        and ""run_trigger"".""function_id"" = ""run"".""function_id""
        and ""run_trigger"".""run_id"" = ""run"".""id""
        and ""run_trigger"".""start"" >= @start
        and ""run_trigger"".""start"" < @stop
    where {where}
)";

            var aggregates = @"count(*) as ""run_count"",
    sum(case when ""status_code"" >= 200 and ""status_code"" < 400 then 1 else 0 end) as ""success_count"",
    sum(case when ""status_code"" = @cancelledStatusCode then 1 else 0 end) as ""cancelled_count"",
    sum(case when ""trigger"" = @scheduled then 1 else 0 end) as ""scheduled_count"",
    sum(case when ""trigger"" = @manual then 1 else 0 end) as ""manual_count"",
    sum(case when ""trigger"" = @api then 1 else 0 end) as ""api_count"",
    sum(""duration"") as ""total_duration"",
    max(""duration"") as ""max_duration"",
    min(""start"") as ""first_start"",
    sum(case when ""start"" <= @scheduleStop then 1 else 0 end) as ""schedule_run_count"",
    max(case when ""function_rank"" = 1 then ""id"" end) as ""function_last_run_id"",
    max(case when ""function_rank"" = 1 then ""start"" end) as ""function_last_run"",
    max(case when ""function_rank"" = 1 then ""status_code"" end) as ""function_last_status_code"",
    max(case when ""task_rank"" = 1 then ""id"" end) as ""task_last_run_id"",
    max(case when ""task_rank"" = 1 then ""start"" end) as ""task_last_run"",
    max(case when ""task_rank"" = 1 then ""status_code"" end) as ""task_last_status_code""";

            return new TrakHoundTableQueryVariants
            {
                [TrakHoundTableDialects.SqlServer] = $@"{runs}
select
    grouping_id(""task_id"", ""bucket"") as ""grouping"",
    ""function_id"",
    ""task_id"",
    ""bucket"",
    {aggregates}
from ""runs""
group by grouping sets ((""function_id""), (""function_id"", ""task_id""), (""function_id"", ""bucket""));",

                // The same "grouping" values as grouping_id (a bit for each of task_id and bucket that isn't grouped on)
                [TrakHoundTableDialects.Ansi] = $@"{runs}
select 3 as ""grouping"", ""function_id"", cast(null as varchar(255)) as ""task_id"", cast(null as bigint) as ""bucket"", {aggregates} from ""runs"" group by ""function_id""
union all
select 1 as ""grouping"", ""function_id"", ""task_id"", cast(null as bigint) as ""bucket"", {aggregates} from ""runs"" group by ""function_id"", ""task_id""
union all
select 2 as ""grouping"", ""function_id"", cast(null as varchar(255)) as ""task_id"", ""bucket"", {aggregates} from ""runs"" group by ""function_id"", ""bucket"";"
            };
        }

        private static string GetTaskKey(string functionId, string taskId) => $"{functionId}:{taskId}";

        /// <summary>
        /// The start of a Scheduled Task's latest Run (read by the Scheduled Tasks Worker to find an occurrence missed while it was stopped)
        /// </summary>
        [ApiGet("tasks/{taskId}/last-run", Description = "Gets the start of a Scheduled Task's latest recorded Run (lastRun is null when it has no Runs).")]
        public async Task<TrakHoundApiResponse> GetTaskLastRun(
            [FromRoute(Description = "The Scheduled Task ID")] string taskId,
            CancellationToken cancellationToken = default
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(taskId)) return BadRequest();

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("taskId", taskId);

            var query = $"select max(\"start\") from \"{FunctionRunTriggerModel.SchemaName}\".\"{FunctionRunTriggerModel.TableName}\" where \"instance_id\" = @instanceId and \"task_id\" = @taskId;";

            var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadSingle<long?>(TrakHoundTableRequest.Ansi(query, parameters), cancellationToken: cancellationToken), cancellationToken, _runTriggerTable);
            if (!tableResponse.Success && tableResponse.StatusCode != 404) return InternalError(tableResponse.Message); // 404 = No Runs

            var lastRun = new FunctionTaskLastRun();
            lastRun.TaskId = taskId;
            lastRun.LastRun = tableResponse.Result.HasValue ? UnixTimeExtensions.FromUnixTime(tableResponse.Result.Value) : null;
            return Ok(lastRun);
        }

        // The Schedule Board columns an Instance starts with (each can be removed)
        private static readonly (string Schedule, string Title)[] _defaultScheduleColumns = new[]
        {
            ("*/5 * * * * *", "5 sec"),
            ("*/10 * * * * *", "10 sec"),
            ("*/30 * * * * *", "30 sec"),
            ("* * * * *", "1 min"),
            ("*/5 * * * *", "5 min"),
            ("*/15 * * * *", "15 min"),
            ("0 * * * *", "1 hour"),
            ("0 0 * * *", "1 day")
        };

        // Marks that the default columns were added (so they don't come back after every column is removed).
        // Not a valid CRON expression, so it can't be added as a column (empty strings are stored as null, so one can't be used)
        private const string _scheduleColumnsInitialized = "#initialized";

        /// <summary>
        /// The Schedule Board columns (columns for Schedules that Tasks use without a saved column are added by the Board)
        /// </summary>
        [ApiGet("schedule-columns", Description = "Lists the Schedule Board's columns: one per Schedule (cron expression), with its title and interval.")]
        public async Task<TrakHoundApiResponse> GetScheduleColumns()
        {
            if (string.IsNullOrEmpty(InstanceId)) return BadRequest();

            var createResponse = await CreateTables(default, _scheduleColumnTable);
            if (!createResponse.Success) return ScheduleColumnsError("Create Table", createResponse.StatusCode, createResponse.Message);

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);

            var query = $"select * from \"{FunctionScheduleColumnModel.SchemaName}\".\"{FunctionScheduleColumnModel.TableName}\" where \"instance_id\" = @instanceId;";
            var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadMultiple<FunctionScheduleColumnModel>(TrakHoundTableRequest.Ansi(query, parameters)), default, _scheduleColumnTable);
            if (!tableResponse.Success && tableResponse.StatusCode != 404) return ScheduleColumnsError("Read", tableResponse.StatusCode, tableResponse.Message); // 404 = No Columns

            var models = tableResponse.Result?.ToList() ?? new List<FunctionScheduleColumnModel>();
            if (models.Count == 0)
            {
                // First use : add the default columns
                models.Add(new FunctionScheduleColumnModel { InstanceId = InstanceId, Schedule = _scheduleColumnsInitialized });
                models.AddRange(_defaultScheduleColumns.Select(o => new FunctionScheduleColumnModel { InstanceId = InstanceId, Schedule = o.Schedule, Title = o.Title }));

                var insertBuilder = new TrakHoundTableRequestBuilder();
                insertBuilder.AppendInsert(models);
                var insertResponse = await Client.Services.Tables.ExecuteNonQuery(insertBuilder.Build());
                if (!insertResponse.Success) return ScheduleColumnsError("Add Default Columns", insertResponse.StatusCode, insertResponse.Message);
            }

            var now = DateTime.UtcNow;
            var columns = new List<FunctionScheduleColumn>();
            foreach (var model in models.Where(o => !string.IsNullOrEmpty(o.Schedule) && o.Schedule != _scheduleColumnsInitialized))
            {
                var column = new FunctionScheduleColumn();
                column.Schedule = model.Schedule;
                column.Title = model.Title;

                var expression = ParseSchedule(model.Schedule, out _);
                if (expression != null) column.IntervalSeconds = GetIntervalSeconds(expression, now);

                columns.Add(column);
            }

            return Ok(columns);
        }

        private TrakHoundApiResponse ScheduleColumnsError(string step, int statusCode, string responseMessage)
        {
            var message = $"Schedule Columns : {step} : {statusCode} : {responseMessage}";
            Log(TrakHoundLogLevel.Error, message);
            return InternalError(message);
        }

        [ApiPost("schedule-columns", Description = "Adds a Schedule Board column for a Schedule (a cron expression with 5 fields, or 6 with seconds), or changes its title.")]
        public async Task<TrakHoundApiResponse> AddScheduleColumn(
            [FromQuery(Description = "The Schedule: a cron expression with 5 fields, or 6 with seconds")] string schedule,
            [FromQuery(Description = "A title for the column")] string title = null
            )
        {
            if (string.IsNullOrEmpty(InstanceId)) return BadRequest();

            var expression = ParseSchedule(schedule, out var error);
            if (expression == null) return BadRequest($"Invalid Schedule : {error}");

            var model = new FunctionScheduleColumnModel();
            model.InstanceId = InstanceId;
            model.Schedule = NormalizeSchedule(schedule);
            model.Title = !string.IsNullOrWhiteSpace(title) ? title.Trim() : null;

            // Replace (to update the Title)
            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("schedule", model.Schedule);

            var deleteQuery = $"delete from \"{FunctionScheduleColumnModel.SchemaName}\".\"{FunctionScheduleColumnModel.TableName}\" where \"instance_id\" = @instanceId and \"schedule\" = @schedule;";
            var deleteResponse = await ExecuteWithTables(() => Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite(deleteQuery, parameters)), default, _scheduleColumnTable);
            if (!deleteResponse.Success) return InternalError(deleteResponse.Message);

            var insertBuilder = new TrakHoundTableRequestBuilder();
            insertBuilder.AppendInsert(new[] { model });
            var insertResponse = await Client.Services.Tables.ExecuteNonQuery(insertBuilder.Build());
            if (!insertResponse.Success) return InternalError(insertResponse.Message);

            var column = new FunctionScheduleColumn();
            column.Schedule = model.Schedule;
            column.Title = model.Title;
            column.IntervalSeconds = GetIntervalSeconds(expression, DateTime.UtcNow);
            return Ok(column);
        }

        [ApiDelete("schedule-columns", Description = "Removes a Schedule Board column. Refused while Scheduled Tasks use its Schedule.")]
        public async Task<TrakHoundApiResponse> DeleteScheduleColumn(
            [FromQuery(Description = "The Schedule (cron expression) of the column to remove")] string schedule
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrWhiteSpace(schedule)) return BadRequest();

            var normalized = NormalizeSchedule(schedule);
            if (normalized == _scheduleColumnsInitialized) return BadRequest();

            // Only empty columns can be removed (a Task's column would come back anyway, as an unnamed column)
            var tasks = await Client.Api.GetJson<IEnumerable<FunctionTask>>(_defaultScheduledTasksRoute);
            var taskCount = tasks?.Count(o => o != null && NormalizeSchedule(o.Schedule) == normalized) ?? 0;
            if (taskCount > 0) return BadRequest($"{taskCount} Scheduled Task{(taskCount > 1 ? "s use" : " uses")} this Schedule : move or delete {(taskCount > 1 ? "them" : "it")} first");

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("schedule", normalized);

            var tableResponse = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite($"delete from \"{FunctionScheduleColumnModel.SchemaName}\".\"{FunctionScheduleColumnModel.TableName}\" where \"instance_id\" = @instanceId and \"schedule\" = @schedule;", parameters));
            if (!tableResponse.Success) return InternalError(tableResponse.Message);

            return Ok();
        }

        private static string NormalizeSchedule(string schedule) => string.Join(' ', (schedule ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Runs started this long after a Schedule occurrence still count as that occurrence's Run
        // (the Scheduled Task Worker polls every 500ms and queues the Run)
        private static readonly TimeSpan _scheduleTolerance = TimeSpan.FromSeconds(5);

        // Occurrences more recent than this aren't counted as Missed yet (the Run may still be queued)
        private static readonly TimeSpan _missedGracePeriod = TimeSpan.FromMinutes(1);

        private const int _maxScheduleOccurrences = 100000;

        /// <summary>
        /// Parses a CRON Schedule with the same format detection as the Scheduled Task Worker (6 fields = with seconds)
        /// </summary>
        private static Cron.CronExpression ParseSchedule(string schedule, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(schedule))
            {
                error = "Schedule is required";
                return null;
            }

            try
            {
                var format = schedule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5 ? Cron.CronFormat.IncludeSeconds : Cron.CronFormat.Standard;
                return Cron.CronExpression.Parse(schedule.Trim(), format);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// The Time Zone of a Scheduled Task (IANA or Windows ID, the server's local Time Zone when empty)
        /// </summary>
        private static TimeZoneInfo GetTimeZone(string timeZoneId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (Exception)
            {
                error = $"Time Zone Not Found : {timeZoneId}";
                return null;
            }
        }

        /// <summary>
        /// Seconds between the next two occurrences (the typical interval of a regular Schedule)
        /// </summary>
        private static double? GetIntervalSeconds(Cron.CronExpression expression, DateTime now, TimeZoneInfo timezone = null)
        {
            timezone ??= TimeZoneInfo.Local;
            var next = expression.GetNextOccurrence(DateTime.SpecifyKind(now, DateTimeKind.Utc), timezone);
            if (next == null) return null;

            var following = expression.GetNextOccurrence(next.Value, timezone);
            if (following == null) return null;

            return (following.Value - next.Value).TotalSeconds;
        }

        /// <summary>
        /// True when a regular Schedule (occurrences every interval) has more occurrences between from and to than the cap, so they
        /// aren't counted one by one. A Schedule that isn't regular across the range (ex. "0,1 * * * * *") returns false and is counted
        /// </summary>
        private static bool ExceedsMaxOccurrences(Cron.CronExpression expression, DateTime from, DateTime to, TimeZoneInfo timezone)
        {
            var first = expression.GetNextOccurrence(from, timezone);
            if (first == null || first.Value > to) return false;

            var second = expression.GetNextOccurrence(first.Value, timezone);
            if (second == null) return false;

            var interval = (second.Value - first.Value).Ticks;
            if (interval <= 0) return false;

            var steps = (to - first.Value).Ticks / interval; // Occurrences after the first, if every interval is the same
            if (steps + 1 <= _maxScheduleOccurrences) return false;

            // Spot check that the occurrences are still every interval across the range
            foreach (var step in new[] { steps / 3, steps / 2 + 1, steps - 1 })
            {
                var occurrence = first.Value.AddTicks(interval * step);
                if (expression.GetNextOccurrence(occurrence, timezone, true) != occurrence) return false;
                if (expression.GetNextOccurrence(occurrence, timezone) != occurrence.AddTicks(interval)) return false;
            }

            return true;
        }

        /// <summary>
        /// Sets the Next Run, and compares the Schedule occurrences since the first Run in the window (firstRun) to the Runs that started
        /// by to + the Schedule Tolerance (scheduleRunCount) to find Missed Runs. to is the end of the range (scheduleStop in GetSummary)
        /// </summary>
        private static void SetTaskSchedule(FunctionTaskSummary taskSummary, DateTime? firstRun, int scheduleRunCount, DateTime now, DateTime to)
        {
            if (string.IsNullOrEmpty(taskSummary.Schedule)) return;

            var expression = ParseSchedule(taskSummary.Schedule, out var scheduleError);
            if (expression == null)
            {
                taskSummary.ScheduleError = scheduleError;
                return;
            }

            // Same Time Zone as the Scheduled Task Worker (the server's local Time Zone when the Task doesn't set one)
            var timezone = GetTimeZone(taskSummary.TimeZone, out var timeZoneError);
            if (timezone == null)
            {
                taskSummary.ScheduleError = timeZoneError;
                return;
            }

            taskSummary.IntervalSeconds = GetIntervalSeconds(expression, now, timezone);

            if (taskSummary.Enabled)
            {
                taskSummary.NextRun = expression.GetNextOccurrence(DateTime.SpecifyKind(now, DateTimeKind.Utc), timezone);
            }

            if (firstRun != null)
            {
                var from = DateTime.SpecifyKind(firstRun.Value, DateTimeKind.Utc) - _scheduleTolerance;
                if (to <= from) return;

                if (ExceedsMaxOccurrences(expression, from, to, timezone)) return; // Too frequent to compare

                var expected = 0;
                var occurrence = expression.GetNextOccurrence(from, timezone);
                while (occurrence != null && occurrence.Value <= to)
                {
                    expected++;
                    if (expected > _maxScheduleOccurrences) return; // Too frequent to compare

                    occurrence = expression.GetNextOccurrence(occurrence.Value, timezone);
                }

                // Every Run started after from (the first Run - the tolerance), so this counts the Runs started by to + the tolerance
                taskSummary.ExpectedRunCount = expected;
                taskSummary.MissedRunCount = Math.Max(0, expected - scheduleRunCount);
            }
        }

        #region "Schedules Export / Import"

        /// <summary>
        /// The Jobs and Scheduled Tasks of every Function (or the ones in functionId : comma separated) as a Document that can be imported on another Instance
        /// </summary>
        [ApiGet("schedules/export", Description = "Exports the Jobs and Scheduled Tasks of every Function (or of the comma separated Function IDs in functionId) as a document that schedules/import can import on another Instance.")]
        public async Task<TrakHoundApiResponse> ExportSchedules(
            [FromQuery(Description = "Comma separated Function IDs to export (every Function when empty)")] string functionId = null
            )
        {
            if (string.IsNullOrEmpty(InstanceId)) return BadRequest();

            var functionFilter = SplitList(functionId);

            var jobsResponse = await ReadJobs();
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);

            var queryParameters = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(functionId)) queryParameters.Add("functionId", functionId);

            var tasksResponse = await Client.Api.Get(Url.Combine(_defaultScheduledTasksRoute, "export"), queryParameters);
            if (!tasksResponse.Success) return InternalError(GetScheduledTasksError("Export", tasksResponse));

            var tasksDocument = Json.Convert<ScheduledTaskExport>(tasksResponse.GetContentUtf8String());

            var document = new FunctionScheduleExport();
            document.Format = FunctionScheduleExport.FormatName;
            document.Version = FunctionScheduleExport.CurrentVersion;
            document.Exported = DateTime.UtcNow;
            document.SourceInstanceId = InstanceId;
            document.Jobs = jobsResponse.Jobs
                .Where(o => functionFilter == null || functionFilter.Contains(o.FunctionId))
                .OrderBy(o => o.FunctionId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            document.Tasks = tasksDocument?.Tasks ?? new List<ScheduledTaskConfiguration>();

            return Ok(document);
        }

        /// <summary>
        /// Imports the Jobs and Scheduled Tasks of an Export Document (or the Tasks of a Scheduled Tasks Api Export). Everything is validated first,
        /// including that each Task's Job is on this Instance or in the Import, and nothing is written when something is invalid or (mode=fail, the default)
        /// a different Job or Task with the same ID exists. mode=skip keeps existing ones, mode=overwrite replaces them. dryRun=true only returns what would happen
        /// </summary>
        [ApiPost("schedules/import", Description = "Imports the Jobs and Scheduled Tasks of an export document. Everything is validated first, and nothing is written if anything is invalid. When a Job or Task with the same ID exists: mode=fail (the default) refuses, mode=skip keeps it, and mode=overwrite replaces it. dryRun=true only reports what would happen.")]
        public async Task<TrakHoundApiResponse> ImportSchedules(
            [FromBody(ContentType = "application/json", Description = "An export document from schedules/export")] FunctionScheduleExport document,
            [FromQuery(Description = "When a Job or Task with the same ID exists: fail (the default) refuses the import, skip keeps it, overwrite replaces it")] string mode = null,
            [FromQuery(Description = "Only report what would happen, without writing anything")] bool dryRun = false
            )
        {
            if (string.IsNullOrEmpty(InstanceId)) return BadRequest();
            if (!ScheduledTaskImport.TryParseMode(mode, out var importMode)) return BadRequest($"Invalid Mode : {mode} (fail, skip, or overwrite)");

            var documentError = FunctionScheduleImport.ValidateDocument(document);
            if (documentError != null) return BadRequest(documentError);

            var result = new FunctionScheduleImportResult();
            result.DryRun = dryRun;
            result.Mode = ScheduledTaskImport.GetModeName(importMode);

            // Jobs
            var jobsResponse = await ReadJobs();
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);

            result.Jobs = FunctionScheduleImport.PlanJobs(document.Jobs, jobsResponse.Jobs, importMode);

            // Tasks (validated by the Scheduled Tasks Api, then checked for their Job)
            var tasks = document.Tasks ?? new List<ScheduledTaskConfiguration>();
            if (tasks.Count > 0)
            {
                var tasksResult = await ImportScheduledTasks(tasks, importMode, true);
                if (tasksResult.Error != null)
                {
                    result.Message = tasksResult.Error;
                    return InternalError(result);
                }

                result.Tasks = tasksResult.Result;

                var availableJobs = FunctionScheduleImport.GetAvailableJobs(jobsResponse.Jobs, result.Jobs);
                FunctionScheduleImport.ApplyTaskErrors(result.Tasks, FunctionScheduleImport.CheckTaskJobs(tasks, availableJobs));
            }

            result.Success = !FunctionScheduleImport.HasErrors(result.Jobs) && (result.Tasks?.Success ?? true);
            if (!result.Success) return BadRequest(result);
            if (dryRun) return Ok(result);

            // Write the Jobs first (so a Task never runs without its Job)
            foreach (var item in result.Jobs.Where(o => o.Job != null))
            {
                item.Job.Updated = DateTime.UtcNow;

                var writeError = await WriteJob(item.Job);
                if (writeError != null)
                {
                    item.Action = ScheduledTaskImportAction.Error;
                    item.Messages ??= new List<string>();
                    item.Messages.Add($"Error writing the Job : {writeError}");
                }
            }

            if (FunctionScheduleImport.HasErrors(result.Jobs))
            {
                result.Success = false;
                result.Message = "Tasks weren't imported because a Job couldn't be written";
                return InternalError(result);
            }

            if (tasks.Count > 0)
            {
                var tasksResult = await ImportScheduledTasks(tasks, importMode, false);
                if (tasksResult.Error != null)
                {
                    result.Success = false;
                    result.Message = tasksResult.Error;
                    return InternalError(result);
                }

                result.Tasks = tasksResult.Result;
                if (!result.Tasks.Success)
                {
                    result.Success = false;
                    return InternalError(result);
                }

                // The function_task rows (Tasks are also found without one, see GetFunctionTasks)
                var linkError = await LinkFunctionTasks(result.Tasks.Tasks.Where(o => o.Action == ScheduledTaskImportAction.Create || o.Action == ScheduledTaskImportAction.Update || o.Action == ScheduledTaskImportAction.Unchanged));
                if (linkError != null) result.Message = $"The Tasks were imported, but linking them to their Functions failed : {linkError}";
            }

            return Ok(result);
        }

        private class ScheduledTasksImportResponse
        {
            public ScheduledTaskImportResult Result { get; set; }
            public string Error { get; set; }
        }

        private async Task<ScheduledTasksImportResponse> ImportScheduledTasks(List<ScheduledTaskConfiguration> tasks, ScheduledTaskImportMode mode, bool dryRun)
        {
            var document = new ScheduledTaskExport();
            document.Format = ScheduledTaskExport.FormatName;
            document.Version = ScheduledTaskExport.CurrentVersion;
            document.Tasks = tasks;

            var queryParameters = new Dictionary<string, string>();
            queryParameters.Add("mode", ScheduledTaskImport.GetModeName(mode));
            queryParameters.Add("dryRun", dryRun.ToString().ToLowerInvariant());

            var response = new ScheduledTasksImportResponse();

            var apiResponse = await Client.Api.Post(Url.Combine(_defaultScheduledTasksRoute, "import"), document, queryParameters: queryParameters);

            // 400 and 500 also return a Result when the Import was read (Invalid Tasks, Conflicts, Write Errors)
            ScheduledTaskImportResult result = null;
            if (apiResponse.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                result = Json.Convert<ScheduledTaskImportResult>(apiResponse.GetContentUtf8String());
            }

            if (result?.Tasks != null) response.Result = result;
            else response.Error = GetScheduledTasksError("Import", apiResponse);

            return response;
        }

        private static string GetScheduledTasksError(string step, TrakHoundApiResponse apiResponse)
        {
            var message = $"Scheduled Tasks {step} : {apiResponse.StatusCode} : {apiResponse.GetContentUtf8String()}";
            if (apiResponse.StatusCode == 404) message += $" (is a version of the TrakHound.ScheduledTasks.Api package with {step} installed at {_defaultScheduledTasksRoute}?)";
            return message;
        }

        /// <summary>
        /// Adds the function_task row of each Task (replacing a row that links it to another Function)
        /// </summary>
        private async Task<string> LinkFunctionTasks(IEnumerable<ScheduledTaskImportItem> tasks)
        {
            foreach (var task in tasks.Where(o => !string.IsNullOrEmpty(o.Id) && !string.IsNullOrEmpty(o.FunctionId)))
            {
                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("taskId", task.Id);

                var deleteQuery = $"delete from \"{FunctionTaskModel.SchemaName}\".\"{FunctionTaskModel.TableName}\" where \"instance_id\" = @instanceId and \"task_id\" = @taskId;";
                var deleteResponse = await ExecuteWithTables(() => Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite(deleteQuery, parameters)), default, _taskTable);
                if (!deleteResponse.Success) return deleteResponse.Message;

                var model = new FunctionTaskModel();
                model.InstanceId = InstanceId;
                model.FunctionId = task.FunctionId;
                model.TaskId = task.Id;

                var insertBuilder = new TrakHoundTableRequestBuilder();
                insertBuilder.AppendInsert(new[] { model });
                var insertResponse = await Client.Services.Tables.ExecuteNonQuery(insertBuilder.Build());
                if (!insertResponse.Success) return insertResponse.Message;
            }

            return null;
        }

        private static HashSet<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        [ApiGet("{functionId}", Description = "Gets a Function's details: name, description, package, run count, average duration and its package ReadMe (which usually describes its input parameters).")]
        public async Task<TrakHoundApiResponse> GetFunction(
            [FromRoute(Description = "The Function ID (from list)")] string functionId
            )
        {
            if (!string.IsNullOrEmpty(functionId))
            {
                var information = await Client.Functions.GetInformation(functionId);
                if (information != null)
                {
                    return Ok(information);
                }
                else
                {
                    return NotFound();
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/tasks", Description = "Lists the Scheduled Tasks that run the Function (schedule, time zone, enabled, and Job). Returns 404 when there are none.")]
		public async Task<TrakHoundApiResponse> GetFunctionTasks(
			[FromRoute(Description = "The Function ID (from list)")] string functionId
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId))
			{
                var query = $"select \"task_id\" from \"{FunctionTaskModel.SchemaName}\".\"{FunctionTaskModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId;";

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);

                var tableResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query, parameters));
                if (tableResponse.Success)
                {
                    var taskConfigurations = new List<FunctionTask>();

                    // Every Task in one request (instead of one request per function_task row)
                    var allTasks = (await Client.Api.GetJson<IEnumerable<FunctionTask>>(_defaultScheduledTasksRoute))?.Where(o => o != null).ToList();

                    if (!tableResponse.Result.IsNullOrEmpty() && !allTasks.IsNullOrEmpty())
                    {
                        // The Tasks of the function_task rows, in row order (a Task is read from its {id}.json file, so the ID is matched ignoring case)
                        var tasksById = new Dictionary<string, FunctionTask>(StringComparer.OrdinalIgnoreCase);
                        foreach (var taskConfiguration in allTasks)
                        {
                            if (!string.IsNullOrEmpty(taskConfiguration.Id)) tasksById.TryAdd(taskConfiguration.Id, taskConfiguration);
                        }

                        foreach (var taskConfigurationId in tableResponse.Result)
                        {
                            if (tasksById.TryGetValue(taskConfigurationId, out var taskConfiguration))
                            {
                                taskConfigurations.Add(taskConfiguration);
                            }
                        }
                    }

                    // Tasks for this Function that were added without the function_task row (ex. written directly to the Scheduled Tasks Volume)
                    if (!allTasks.IsNullOrEmpty())
                    {
                        foreach (var taskConfiguration in allTasks)
                        {
                            if (taskConfiguration != null && string.Equals(taskConfiguration.FunctionId, functionId, StringComparison.OrdinalIgnoreCase) && !taskConfigurations.Any(o => o.Id == taskConfiguration.Id))
                            {
                                taskConfigurations.Add(taskConfiguration);
                            }
                        }
                    }

                    if (taskConfigurations.Count > 0)
                    {
                        return Ok(taskConfigurations);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return InternalError(tableResponse.Message);
                }
            }
			else
			{
				return BadRequest();
			}
		}

        [ApiGet("runs", Description = "Lists the Runs of every Function that started between start and stop (dates), with each Run's status, status code, start, end and duration. Sorted by start (sortOrder 1 = oldest first, -1 = newest first) and paged with skip and take (up to 10,000). Returns 404 when there are none.")]
        public async Task<TrakHoundApiResponse> GetFunctionRuns(
            [FromQuery(Description = "The earliest Run start to include, as a date and time")] string start,
            [FromQuery(Description = "Runs that start before this are included, as a date and time")] string stop,
            [FromQuery(Description = "Runs to skip (for paging)")] int skip = 0,
            [FromQuery(Description = "The most Runs to return (up to 10,000)")] int take = 100,
            [FromQuery(Description = "1 = oldest first, -1 = newest first")] int sortOrder = (int)TrakHoundSortOrder.Ascending,
            CancellationToken cancellationToken = default
            )
        {
            if (!string.IsNullOrEmpty(InstanceId))
            {
                var startTimestamp = start.ToDateTime().ToUnixTime();
                var stopTimestamp = stop.ToDateTime().ToUnixTime();

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("start", startTimestamp);
                parameters.Add("stop", stopTimestamp);

                var conditions = new List<string>();
                conditions.Add("\"instance_id\" = @instanceId");
                conditions.Add("\"start\" >= @start");
                conditions.Add("\"start\" < @stop");

                var query = $"select * from \"{FunctionRunModel.SchemaName}\".\"{FunctionRunModel.TableName}\" where {string.Join(" and ", conditions)}{GetRunsOrder(sortOrder)};";

                var tableResponse = await Client.Services.Tables.ReadMultiple<FunctionRunModel>(TrakHoundTableRequest.Ansi(query, parameters, Math.Max(0, skip), Math.Clamp(take, 1, _maxRunsTake)), cancellationToken: cancellationToken);
                if (tableResponse.Success)
                {
                    if (!tableResponse.Result.IsNullOrEmpty())
                    {
                        var functionRuns = new List<FunctionRun>();

                        foreach (var functionRunModel in tableResponse.Result)
                        {
                            var functionRun = new FunctionRun();
                            functionRun.Id = functionRunModel.Id;
                            functionRun.FunctionId = functionRunModel.FunctionId;
                            functionRun.StatusCode = functionRunModel.StatusCode;
                            functionRun.Status = GetRunStatus(functionRunModel.StatusCode);
                            functionRun.Start = functionRunModel.Start;
                            functionRun.End = functionRunModel.End;
                            functionRun.Duration = functionRunModel.Duration;

                            functionRuns.Add(functionRun);
                        }

                        return Ok(functionRuns);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return InternalError(tableResponse.Message);
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/runs", Description = "Lists the Runs of a Function that started between start and stop (dates), with each Run's status, status code, start, end and duration. Sorted by start (sortOrder 1 = oldest first, -1 = newest first) and paged with skip and take (up to 10,000). Returns 404 when there are none.")]
        public async Task<TrakHoundApiResponse> GetFunctionRuns(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromQuery(Description = "The earliest Run start to include, as a date and time")] string start,
            [FromQuery(Description = "Runs that start before this are included, as a date and time")] string stop,
            [FromQuery(Description = "Runs to skip (for paging)")] int skip = 0,
            [FromQuery(Description = "The most Runs to return (up to 10,000)")] int take = 100,
            [FromQuery(Description = "1 = oldest first, -1 = newest first")] int sortOrder = (int)TrakHoundSortOrder.Ascending,
            CancellationToken cancellationToken = default
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId))
            {
                var startTimestamp = start.ToDateTime().ToUnixTime();
                var stopTimestamp = stop.ToDateTime().ToUnixTime();

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("start", startTimestamp);
                parameters.Add("stop", stopTimestamp);

                var conditions = new List<string>();
                conditions.Add("\"instance_id\" = @instanceId");
                conditions.Add("\"function_id\" = @functionId");
                conditions.Add("\"start\" >= @start");
                conditions.Add("\"start\" < @stop");

                var query = $"select * from \"{FunctionRunModel.SchemaName}\".\"{FunctionRunModel.TableName}\" where {string.Join(" and ", conditions)}{GetRunsOrder(sortOrder)};";

                var tableResponse = await Client.Services.Tables.ReadMultiple<FunctionRunModel>(TrakHoundTableRequest.Ansi(query, parameters, Math.Max(0, skip), Math.Clamp(take, 1, _maxRunsTake)), cancellationToken: cancellationToken);
                if (tableResponse.Success)
                {
                    if (!tableResponse.Result.IsNullOrEmpty())
                    {
                        var functionRuns = new List<FunctionRun>();

                        foreach (var functionRunModel in tableResponse.Result)
                        {
                            var functionRun = new FunctionRun();
                            functionRun.Id = functionRunModel.Id;
                            functionRun.FunctionId = functionRunModel.FunctionId;
                            functionRun.StatusCode = functionRunModel.StatusCode;
                            functionRun.Status = GetRunStatus(functionRunModel.StatusCode);
                            functionRun.Start = functionRunModel.Start;
                            functionRun.End = functionRunModel.End;
                            functionRun.Duration = functionRunModel.Duration;

                            functionRuns.Add(functionRun);
                        }

                        return Ok(functionRuns);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return InternalError(tableResponse.Message);
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/runs/{runId}", Description = "Gets a Run of a Function: its status, status code, start, end, duration, what started it (trigger and Scheduled Task), the package version that ran, its Job and the CPU it used.")]
        public async Task<TrakHoundApiResponse> GetFunctionRun(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                var runTable = $"\"{FunctionRunModel.SchemaName}\".\"{FunctionRunModel.TableName}\"";
                var triggerTable = $"\"{FunctionRunTriggerModel.SchemaName}\".\"{FunctionRunTriggerModel.TableName}\"";
                var detailTable = $"\"{FunctionRunDetailModel.SchemaName}\".\"{FunctionRunDetailModel.TableName}\"";

                // Runs recorded before the trigger and detail tables have no rows there (so their columns are null)
                var query = $@"select
    ""run"".""id"",
    ""run"".""function_id"",
    ""run"".""status_code"",
    ""run"".""start"",
    ""run"".""end"",
    ""run"".""duration"",
    ""run_trigger"".""trigger"",
    ""run_trigger"".""task_id"",
    ""run_detail"".""package_version"",
    ""run_detail"".""job_id"",
    ""run_detail"".""cpu_time"",
    ""run_detail"".""cpu_usage""
from {runTable} as ""run""
left join {triggerTable} as ""run_trigger""
    on ""run_trigger"".""instance_id"" = ""run"".""instance_id"" and ""run_trigger"".""function_id"" = ""run"".""function_id"" and ""run_trigger"".""run_id"" = ""run"".""id""
left join {detailTable} as ""run_detail""
    on ""run_detail"".""instance_id"" = ""run"".""instance_id"" and ""run_detail"".""function_id"" = ""run"".""function_id"" and ""run_detail"".""run_id"" = ""run"".""id""
where ""run"".""instance_id"" = @instanceId and ""run"".""function_id"" = @functionId and ""run"".""id"" = @runId;";

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("runId", runId);

                var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadSingle<FunctionRunRow>(TrakHoundTableRequest.Ansi(query, parameters)), default, _runTable, _runTriggerTable, _runDetailTable);
                if (tableResponse.Success || tableResponse.StatusCode == 404)
                {
                    if (tableResponse.Result != null)
                    {
                        var row = tableResponse.Result;
                        var functionRun = new FunctionRun();
                        functionRun.Id = row.Id;
                        functionRun.FunctionId = row.FunctionId;
                        functionRun.StatusCode = row.StatusCode;
                        functionRun.Status = GetRunStatus(row.StatusCode);
                        functionRun.Start = row.Start;
                        functionRun.End = row.End;
                        functionRun.Duration = row.Duration;
                        functionRun.Trigger = row.Trigger;
                        functionRun.TaskId = row.TaskId;
                        functionRun.PackageVersion = row.PackageVersion;
                        functionRun.JobId = row.JobId;
                        functionRun.CpuTime = row.CpuTime;
                        functionRun.CpuUsage = row.CpuUsage;

                        return Ok(functionRun);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return InternalError(tableResponse.Message);
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/runs/{runId}/input", Description = "Gets a completed Run's input parameters (names and values, after a Job's parameters are applied). Returns 404 when the Run had no input or isn't recorded.")]
        public async Task<TrakHoundApiResponse> GetFunctionRunInput(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("runId", runId);

                var query = $"select * from \"{FunctionRunInputModel.SchemaName}\".\"{FunctionRunInputModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"run_id\" = @runId;";

                // Creates the Table first (it doesn't exist until a Run with Input completes)
                var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadMultiple<FunctionRunInputModel>(TrakHoundTableRequest.Ansi(query, parameters)), default, _runInputTable);
                if (!tableResponse.Success && tableResponse.StatusCode != 404) return InternalError(tableResponse.Message);
                if (tableResponse.Result.IsNullOrEmpty()) return NotFound();

                var input = new Dictionary<string, string>();
                foreach (var parameter in tableResponse.Result) input[parameter.Name] = parameter.Value;
                return Ok(input);
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/runs/{runId}/output", Description = "Gets a completed Run's output parameters (names and values, recorded by {functionId}/run). Returns 404 when the Run had no output or isn't recorded.")]
        public async Task<TrakHoundApiResponse> GetFunctionRunOutput(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("runId", runId);

                var query = $"select * from \"{FunctionRunOutputModel.SchemaName}\".\"{FunctionRunOutputModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"run_id\" = @runId;";

                // Creates the Table first (it doesn't exist until a Run with Output completes)
                var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadMultiple<FunctionRunOutputModel>(TrakHoundTableRequest.Ansi(query, parameters)), default, _runOutputTable);
                if (!tableResponse.Success && tableResponse.StatusCode != 404) return InternalError(tableResponse.Message);
                if (tableResponse.Result.IsNullOrEmpty()) return NotFound();

                var output = new Dictionary<string, string>();
                foreach (var parameter in tableResponse.Result) output[parameter.Name] = parameter.Value;
                return Ok(output);
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiGet("{functionId}/runs/{runId}/status", Description = "Gets a completed Run's statuses: Started at its start, then Completed, Stopped or Error at its end. Runs are recorded when they complete, so a running Run returns 404 (use the Stream at the same route for live status).")]
        public async Task<TrakHoundApiResponse> GetFunctionRunStatus(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                // Runs are recorded when they complete, so the Statuses are when it Started and how it ended
                var query = $"select * from \"{FunctionRunModel.SchemaName}\".\"{FunctionRunModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"id\" = @runId;";

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("runId", runId);

                var tableResponse = await Client.Services.Tables.ReadSingle<FunctionRunModel>(TrakHoundTableRequest.Ansi(query, parameters));
                if (!tableResponse.Success && tableResponse.StatusCode != 404) return InternalError(tableResponse.Message);
                if (tableResponse.Result == null) return NotFound();

                var run = tableResponse.Result;
                var statuses = new List<FunctionStatus>();
                statuses.Add(new FunctionStatus { Status = TrakHoundFunctionStatusType.Started, Timestamp = run.Start });
                statuses.Add(new FunctionStatus { Status = GetRunStatus(run.StatusCode).ConvertEnum<TrakHoundFunctionStatusType>(), Timestamp = run.End });
                return Ok(statuses);
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiStream("{functionId}/runs/{runId}/status", Description = "Streams each status of a Run as it happens (Started, Running, Completed, Error, ...). Subscribe before the Run starts to see all of them: pass the same runId to {functionId}/run.")]
        public async Task<ITrakHoundStream<TrakHoundApiResponse>> SubscribeFunctionRunStatus(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                // Each Status of the Run as it happens (the Run can be started after subscribing, with the same runId)
                var statusStream = await Client.Functions.SubscribeRunStatus(runId);
                if (statusStream != null)
                {
                    return new TrakHoundStream<TrakHoundFunctionStatusResponse, TrakHoundApiResponse>(statusStream, onReceived: status =>
                    {
                        var functionStatus = new FunctionStatus();
                        functionStatus.Status = status.Status;
                        functionStatus.Timestamp = status.Timestamp.ToDateTime();
                        return Ok(new[] { functionStatus });
                    });
                }
            }

            return null;
        }

        [ApiGet("{functionId}/runs/{runId}/log", Description = "Gets a Run's log (recorded when the Run completes) at minimumLevel or more severe: Critical, Error, Warning, Information, Debug or Trace (the default).")]
		public async Task<TrakHoundApiResponse> GetFunctionRunLog(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Run ID")] string runId,
			[FromQuery(Description = "The lowest log level to include: Critical, Error, Warning, Information, Debug or Trace")] string minimumLevel = "Trace",
			CancellationToken cancellationToken = default
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
			{
                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("instanceId", InstanceId);
                parameters.Add("functionId", functionId);
                parameters.Add("runId", runId);
                parameters.Add("level", (int)(minimumLevel.ConvertEnum<TrakHoundLogLevel>()));

                var conditions = new List<string>();
                conditions.Add("\"instance_id\" = @instanceId");
                conditions.Add("\"function_id\" = @functionId");
                conditions.Add("\"run_id\" = @runId");
                conditions.Add("\"level\" <= @level");

                var query = $"select * from \"{FunctionLogModel.SchemaName}\".\"{FunctionLogModel.TableName}\" where {string.Join(" and ", conditions)};";

                var tableResponse = await Client.Services.Tables.ReadMultiple<FunctionLogModel>(TrakHoundTableRequest.Ansi(query, parameters), cancellationToken: cancellationToken);
                if (tableResponse.Success)
                {
                    if (!tableResponse.Result.IsNullOrEmpty())
                    {
                        var functionLogs = new List<FunctionLog>();

                        foreach (var functionLogModel in tableResponse.Result)
                        {
                            var functionLog = new FunctionLog();
                            functionLog.Level = (TrakHoundLogLevel)functionLogModel.Level;
                            functionLog.Message = functionLogModel.Message;
                            functionLog.Timestamp = functionLogModel.Timestamp;
                            functionLogs.Add(functionLog);
                        }

                        return Ok(functionLogs);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return InternalError(tableResponse.Message);
                }
            }
			else
			{
				return BadRequest();
			}
		}

		[ApiStream("{functionId}/runs/{runId}/log", Description = "Streams a Run's log as it is written, at minimumLevel or more severe (default Trace). Subscribe before the Run starts to see every line: pass the same runId to {functionId}/run.")]
		public async Task<ITrakHoundStream<TrakHoundApiResponse>> SubscribeFunctionRunLog(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Run ID")] string runId,
			[FromQuery(Description = "The lowest log level to include: Critical, Error, Warning, Information, Debug or Trace")] string minimumLevel = "Trace"
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
			{
                // Each Log Item of the Run as it is written (the Run can be started after subscribing, with the same runId)
                var level = minimumLevel.ConvertEnum<TrakHoundLogLevel>();
                var logStream = await Client.Functions.SubscribeRunLog(runId, level);
                if (logStream != null)
                {
                    return new TrakHoundStream<IEnumerable<TrakHoundLogItem>, TrakHoundApiResponse>(logStream, onReceived: items =>
                    {
                        var functionLogs = new List<FunctionLog>();
                        foreach (var item in items ?? Enumerable.Empty<TrakHoundLogItem>())
                        {
                            var functionLog = new FunctionLog();
                            functionLog.Level = item.LogLevel;
                            functionLog.Message = item.Message;
                            functionLog.Timestamp = item.Timestamp.ToDateTime();
                            functionLogs.Add(functionLog);
                        }
                        return Ok(functionLogs);
                    });
                }
			}

            return null;
		}


		[ApiPost("{functionId}/run", Description = "Runs a Function and waits for it to complete. The body holds its input parameters (names and string values); with jobId, the Job's parameters are used and the body's are applied on top. Returns the Run's status code, output parameters and log, and records the Run as triggered by the Scheduled Task in taskId, by trigger=manual, or otherwise by the api. runId and timestamp set the Run's ID and start time.")]
        public async Task<TrakHoundApiResponse> RunFunction(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromBody(ContentType = "application/json", Description = "Input parameters (names and string values), applied on top of the Job's parameters when jobId is set")] Dictionary<string, string> inputParameters = null,
            [FromQuery(Description = "An ID for the Run (generated when empty). Subscribe to the Run's status and log Streams with it before starting the Run")] string runId = null,
            [FromQuery(Description = "The Run's start time (default now)")] string timestamp = null,
            [FromQuery(Description = "The Scheduled Task that started the Run (recorded as a scheduled Run)")] string taskId = null,
            [FromQuery(Description = "What started the Run: manual (otherwise it is recorded as api)")] string trigger = null,
            [FromQuery(Description = "A Job whose parameters the Function is run with")] string jobId = null
            )
        {
            var apiResponse = new TrakHoundApiResponse();

            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId))
            {
                // A Job's Parameters (read when the Run starts, so Job changes apply to the next Run). Parameters in the
                // Request Body are applied on top
                if (!string.IsNullOrEmpty(jobId))
                {
                    var jobResponse = await ReadJob(functionId, jobId);
                    if (jobResponse.Error != null) return InternalError(jobResponse.Error);
                    if (jobResponse.Job == null) return NotFound($"Job Not Found : {jobId} (Function ID = {functionId})");

                    var jobParameters = new Dictionary<string, string>(jobResponse.Job.Parameters ?? new Dictionary<string, string>());
                    if (inputParameters != null)
                    {
                        foreach (var parameter in inputParameters) jobParameters[parameter.Key] = parameter.Value;
                    }
                    inputParameters = jobParameters;
                }

                var started = timestamp != null ? timestamp.ToDateTime() : DateTime.UtcNow;

                var runResponse = await Client.Functions.Run(functionId, inputParameters, runId, started.ToUnixTime());

                var completed = runResponse.Completed.ToDateTime();

                var functionRun = new FunctionRunModel();
                functionRun.InstanceId = InstanceId;
                functionRun.FunctionId = functionId;
                functionRun.Id = runResponse.Id;
                functionRun.StatusCode = runResponse.StatusCode;
                functionRun.Start = started;
                functionRun.End = completed;
                functionRun.Duration = completed - started;

                // The Tables are created once per process (see CreateTables), not with every Run
                var queryBuilder = new TrakHoundTableRequestBuilder();
                queryBuilder.AppendInsert(new[] { functionRun });

                // What started the Run (for the per Trigger / per Scheduled Task metrics)
                var runTrigger = new FunctionRunTriggerModel();
                runTrigger.InstanceId = InstanceId;
                runTrigger.FunctionId = functionId;
                runTrigger.RunId = runResponse.Id;
                runTrigger.Trigger = GetRunTrigger(taskId, trigger);
                runTrigger.TaskId = !string.IsNullOrEmpty(taskId) ? taskId : null;
                runTrigger.Start = started;

                queryBuilder.AppendInsert(new[] { runTrigger });

                // The Package version that ran, the Job and the CPU the Run consumed (read by {functionId}/runs/{runId})
                var runDetail = new FunctionRunDetailModel();
                runDetail.InstanceId = InstanceId;
                runDetail.FunctionId = functionId;
                runDetail.RunId = runResponse.Id;
                runDetail.PackageVersion = runResponse.PackageVersion;
                runDetail.JobId = !string.IsNullOrEmpty(jobId) ? jobId : null;
                runDetail.CpuTime = runResponse.CpuTime;
                runDetail.CpuUsage = GetCpuUsage(runResponse.CpuTime, functionRun.Duration);
                runDetail.Start = started;

                queryBuilder.AppendInsert(new[] { runDetail });

                // Input Parameters, after the Job's are applied (read by {functionId}/runs/{runId}/input). Parameters the Function
                // describes as Secret are left out
                var secretParameters = await GetSecretParameters(functionId);
                var savedInput = inputParameters?.Where(o => !secretParameters.Contains(o.Key)).ToList();
                if (!savedInput.IsNullOrEmpty())
                {
                    var inputModels = savedInput.Select(o => new FunctionRunInputModel
                    {
                        RunId = runResponse.Id,
                        InstanceId = InstanceId,
                        FunctionId = functionId,
                        Name = o.Key,
                        Value = o.Value
                    }).ToList();

                    queryBuilder.AppendInsert(inputModels);
                }

                // Output Parameters (read by {functionId}/runs/{runId}/output)
                if (!runResponse.Parameters.IsNullOrEmpty())
                {
                    var outputModels = runResponse.Parameters.Select(o => new FunctionRunOutputModel
                    {
                        RunId = runResponse.Id,
                        InstanceId = InstanceId,
                        FunctionId = functionId,
                        Name = o.Key,
                        Value = o.Value
                    }).ToList();

                    queryBuilder.AppendInsert(outputModels);
                }

                if (!runResponse.Logs.IsNullOrEmpty())
                {
                    var logModels = new List<FunctionLogModel>();
                    foreach (var log in runResponse.Logs)
                    {
                        var logModel = new FunctionLogModel();
                        logModel.RunId = runResponse.Id;
                        logModel.InstanceId = InstanceId;
                        logModel.FunctionId = functionId;
                        logModel.Level = (int)log.LogLevel;
                        logModel.Message = log.Message;
                        logModel.Timestamp = log.Timestamp.ToDateTime();
                        logModels.Add(logModel);
                    }

                    queryBuilder.AppendInsert(logModels);
                }

                var insertQuery = queryBuilder.Build();
                var tableResponse = await ExecuteWithTables(() => Client.Services.Tables.ExecuteNonQuery(insertQuery), default, _runTable, _runTriggerTable, _runDetailTable, _runInputTable, _runOutputTable, _logTable);
                if (tableResponse.Success)
                {
                    Log(TrakHoundLogLevel.Information, "Function Run Written Successfully to Table");
                }
                else
                {
                    Log(TrakHoundLogLevel.Error, $"Error Writing Function Run to Table : {tableResponse.StatusCode} : {tableResponse.Message}");
                }

                var jsonResponse = new TrakHoundFunctionJsonResponse(runResponse);
                
                apiResponse.StatusCode = runResponse.StatusCode;
                apiResponse.Success = true;
                apiResponse.ContentType = "application/json";
                apiResponse.Content = TrakHoundApiResponse.GetContentStream(System.Text.Encoding.UTF8.GetBytes(jsonResponse.ToJson(true)));
            }
            else
            {
                apiResponse.StatusCode = 400;
            }

            return apiResponse;
        }

        [ApiPost("{functionId}/runs/{runId}/cancel", Description = "Cancels a Run that is still running. Returns 404 when it isn't running.")]
        public async Task<TrakHoundApiResponse> CancelFunctionRun(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Run ID")] string runId
            )
        {
            if (!string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                if (await Client.Functions.CancelRun(functionId, runId))
                {
                    return Ok();
                }
                else
                {
                    return NotFound();
                }
            }
            else
            {
                return BadRequest();
            }
        }


		#region "Jobs"

        private class JobsResponse
        {
            public List<FunctionJob> Jobs { get; set; } = new List<FunctionJob>();
            public string Error { get; set; }
        }

        private class JobResponse
        {
            public FunctionJob Job { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// The Jobs of a Function (or of every Function when functionId is empty)
        /// </summary>
        private async Task<JobsResponse> ReadJobs(string functionId = null, CancellationToken cancellationToken = default)
        {
            var response = new JobsResponse();

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);

            var query = $"select * from \"{FunctionJobModel.SchemaName}\".\"{FunctionJobModel.TableName}\" where \"instance_id\" = @instanceId";
            if (!string.IsNullOrEmpty(functionId))
            {
                parameters.Add("functionId", functionId);
                query += " and \"function_id\" = @functionId";
            }

            query += ";";
            var tableResponse = await RunWithTables(() => Client.Services.Tables.ReadMultiple<FunctionJobModel>(TrakHoundTableRequest.Ansi(query, parameters), cancellationToken: cancellationToken), cancellationToken, _jobTable);
            if (!tableResponse.Success && tableResponse.StatusCode != 404) // 404 = No Jobs
            {
                response.Error = tableResponse.Message;
                return response;
            }

            if (!tableResponse.Result.IsNullOrEmpty())
            {
                response.Jobs = tableResponse.Result.Select(FunctionJob.FromModel).Where(o => o != null).ToList();
            }

            return response;
        }

        private async Task<JobResponse> ReadJob(string functionId, string jobId)
        {
            var jobsResponse = await ReadJobs(functionId);
            return new JobResponse
            {
                Job = jobsResponse.Jobs.FirstOrDefault(o => o.Id == jobId),
                Error = jobsResponse.Error
            };
        }

        /// <summary>
        /// Validates a Job's Name (required, and unique for the Function)
        /// </summary>
        private static string ValidateJob(FunctionJob job, IEnumerable<FunctionJob> existingJobs)
        {
            if (job == null) return "Job is required";
            if (string.IsNullOrWhiteSpace(job.Name)) return "Job Name is required";

            var duplicate = existingJobs.FirstOrDefault(o => o.Id != job.Id && string.Equals(o.Name?.Trim(), job.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (duplicate != null) return $"A Job named \"{duplicate.Name}\" already exists for this Function";

            return null;
        }

        private async Task<string> WriteJob(FunctionJob job)
        {
            var model = job.ToModel(InstanceId);

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("functionId", job.FunctionId);
            parameters.Add("id", job.Id);

            var deleteQuery = $"delete from \"{FunctionJobModel.SchemaName}\".\"{FunctionJobModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"id\" = @id;";
            var deleteResponse = await ExecuteWithTables(() => Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite(deleteQuery, parameters)), default, _jobTable);
            if (!deleteResponse.Success) return deleteResponse.Message;

            var insertBuilder = new TrakHoundTableRequestBuilder();
            insertBuilder.AppendInsert(new[] { model });
            var insertResponse = await Client.Services.Tables.ExecuteNonQuery(insertBuilder.Build());
            return insertResponse.Success ? null : insertResponse.Message;
        }

        [ApiGet("{functionId}/jobs", Description = "Lists the Jobs of a Function (named sets of input parameters that Runs and Scheduled Tasks can use), sorted by name.")]
        public async Task<TrakHoundApiResponse> GetJobs(
            [FromRoute(Description = "The Function ID (from list)")] string functionId
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId)) return BadRequest();

            var jobsResponse = await ReadJobs(functionId);
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);

            return Ok(jobsResponse.Jobs.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase));
        }

        [ApiGet("{functionId}/jobs/{jobId}", Description = "Gets a Job of a Function: its name, description and parameters.")]
        public async Task<TrakHoundApiResponse> GetJob(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Job ID (from {functionId}/jobs)")] string jobId
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(jobId)) return BadRequest();

            var jobResponse = await ReadJob(functionId, jobId);
            if (jobResponse.Error != null) return InternalError(jobResponse.Error);

            return jobResponse.Job != null ? Ok(jobResponse.Job) : NotFound();
        }

        [ApiPost("{functionId}/jobs", Description = "Adds a Job (a named set of input parameters) to a Function. The name is required and must be unique for the Function. The Job's ID is the one in the body, or is made from the name.")]
        public async Task<TrakHoundApiResponse> AddJob(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromBody(ContentType = "application/json", Description = "The Job: name (required and unique for the Function), description, parameters (names and string values), and optionally an id")] FunctionJob job
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId) || job == null) return BadRequest();

            var jobsResponse = await ReadJobs(functionId);
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);

            var requestedId = job.Id?.Trim();
            job.Id = null;
            job.FunctionId = functionId;
            job.Name = job.Name?.Trim();
            job.Parameters = FunctionJob.CleanParameters(job.Parameters);
            job.Updated = DateTime.UtcNow;

            var error = ValidateJob(job, jobsResponse.Jobs);
            if (error != null) return BadRequest(error);

            // The ID is the same on every Instance (Scheduled Tasks reference it, and an exported Task finds its Job by it) : the one requested, or one made from the Name
            if (!string.IsNullOrEmpty(requestedId))
            {
                var idError = ConfigurationId.Validate(requestedId, "Job ID");
                if (idError != null) return BadRequest(idError);
                if (jobsResponse.Jobs.Any(o => string.Equals(o.Id, requestedId, StringComparison.OrdinalIgnoreCase))) return BadRequest($"A Job with the ID \"{requestedId}\" already exists for this Function");
                job.Id = requestedId;
            }
            else
            {
                job.Id = ConfigurationId.MakeUnique(ConfigurationId.FromName(job.Name, "job"), jobsResponse.Jobs.Select(o => o.Id));
            }

            var writeError = await WriteJob(job);
            return writeError == null ? Ok(job) : InternalError(writeError);
        }

        /// <summary>
        /// Updates a Job's Name, Description, and Parameters (Scheduled Tasks using the Job get the new Parameters on their next Run)
        /// </summary>
        [ApiPost("{functionId}/jobs/{jobId}", Description = "Updates a Job's name, description and parameters. Scheduled Tasks that use the Job get the new parameters on their next Run.")] // POST (the Api Client can only send a Request Body with POST)
        public async Task<TrakHoundApiResponse> UpdateJob(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Job ID (from {functionId}/jobs)")] string jobId,
            [FromBody(ContentType = "application/json", Description = "The Job's new name, description and parameters")] FunctionJob job
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(jobId) || job == null) return BadRequest();

            var jobsResponse = await ReadJobs(functionId);
            if (jobsResponse.Error != null) return InternalError(jobsResponse.Error);
            if (!jobsResponse.Jobs.Any(o => o.Id == jobId)) return NotFound();

            job.Id = jobId;
            job.FunctionId = functionId;
            job.Name = job.Name?.Trim();
            job.Parameters = FunctionJob.CleanParameters(job.Parameters);
            job.Updated = DateTime.UtcNow;

            var error = ValidateJob(job, jobsResponse.Jobs);
            if (error != null) return BadRequest(error);

            var writeError = await WriteJob(job);
            return writeError == null ? Ok(job) : InternalError(writeError);
        }

        /// <summary>
        /// Deletes a Job (refused while Scheduled Tasks use it)
        /// </summary>
        [ApiDelete("{functionId}/jobs/{jobId}", Description = "Deletes a Job. Refused while Scheduled Tasks use it.")]
        public async Task<TrakHoundApiResponse> DeleteJob(
            [FromRoute(Description = "The Function ID (from list)")] string functionId,
            [FromRoute(Description = "The Job ID (from {functionId}/jobs)")] string jobId
            )
        {
            if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(jobId)) return BadRequest();

            var tasks = await Client.Api.GetJson<IEnumerable<FunctionTask>>(_defaultScheduledTasksRoute);
            var usedBy = tasks?.Where(o => o != null && o.JobId == jobId && string.Equals(o.FunctionId, functionId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!usedBy.IsNullOrEmpty())
            {
                var names = string.Join(", ", usedBy.Select(o => !string.IsNullOrEmpty(o.Description) ? o.Description : $"{o.Schedule} ({o.Id})"));
                return BadRequest($"{usedBy.Count} Scheduled Task{(usedBy.Count > 1 ? "s use" : " uses")} this Job : {names}. Delete {(usedBy.Count > 1 ? "them" : "it")} first");
            }

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("instanceId", InstanceId);
            parameters.Add("functionId", functionId);
            parameters.Add("id", jobId);

            var tableResponse = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite($"delete from \"{FunctionJobModel.SchemaName}\".\"{FunctionJobModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"id\" = @id;", parameters));
            return tableResponse.Success ? Ok() : InternalError(tableResponse.Message);
        }

        #endregion

		[ApiPost("{functionId}/tasks", Description = "Adds a Scheduled Task that runs the Function on a schedule (a cron expression with 5 fields, or 6 with seconds) in timeZone (IANA or Windows ID, the server's when empty), optionally with a Job's parameters. runMissed runs it once when the Scheduled Tasks Worker starts if a Run was missed while it was stopped.")]
		public async Task<TrakHoundApiResponse> AddFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromQuery(Description = "A description of the Scheduled Task")] string description,
			[FromQuery(Description = "When it runs: a cron expression with 5 fields, or 6 with seconds")] string schedule,
			[FromQuery(Description = "Whether it runs on its Schedule (default true)")] bool enabled = true,
			[FromQuery(Description = "The Time Zone of the Schedule: an IANA or Windows ID (the server's when empty)")] string timeZone = null,
			[FromQuery(Description = "Run once when the Scheduled Tasks Worker starts if a Run was missed while it was stopped")] bool runMissed = false,
			[FromQuery(Description = "A Job whose parameters the Scheduled Task runs the Function with")] string jobId = null
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId))
			{
                var scheduleError = ValidateSchedule(schedule);
                if (scheduleError != null) return BadRequest(scheduleError);

                if (GetTimeZone(timeZone, out var timeZoneError) == null) return BadRequest(timeZoneError);

                if (!string.IsNullOrEmpty(jobId))
                {
                    var jobResponse = await ReadJob(functionId, jobId);
                    if (jobResponse.Error != null) return InternalError(jobResponse.Error);
                    if (jobResponse.Job == null) return BadRequest($"Job Not Found : {jobId}");
                }

                var task = new FunctionTask();
                task.Id = Guid.NewGuid().ToString();
                task.Description = description;
                task.Enabled = enabled;
                task.FunctionId = functionId;
                task.Schedule = schedule.Trim();
                task.TimeZone = !string.IsNullOrWhiteSpace(timeZone) ? timeZone.Trim() : null;
                task.RunMissed = runMissed;
                task.JobId = !string.IsNullOrEmpty(jobId) ? jobId : null;

                var apiResponse = await Client.Api.Post(_defaultScheduledTasksRoute, task);
                if (apiResponse.Success)
                {
                    var taskModel = new FunctionTaskModel();
                    taskModel.InstanceId = InstanceId;
                    taskModel.FunctionId = functionId;
                    taskModel.TaskId = task.Id;

                    var queryBuilder = new TrakHoundTableRequestBuilder();
                    queryBuilder.AppendInsert(new[] { taskModel });

                    var insertQuery = queryBuilder.Build();
                    var tableResponse = await ExecuteWithTables(() => Client.Services.Tables.ExecuteNonQuery(insertQuery), default, _taskTable);
                    if (tableResponse.Success)
                    {
                        return Ok(task);
                    }
                    else
                    {
                        return InternalError(tableResponse.Message);
                    }
                }
                else
                {
                    return InternalError(apiResponse.GetContentUtf8String());
                }
			}
			else
			{
				return BadRequest();
			}
		}

		/// <summary>
		/// Changes the Schedule (and optionally the Description) of a Scheduled Task. The Scheduled Task Worker
		/// restarts the Task's Engine when its configuration changes
		/// </summary>
		[ApiPut("{functionId}/tasks/{taskConfigurationId}", Description = "Changes a Scheduled Task's schedule (a cron expression), and its description when one is given.")]
		public async Task<TrakHoundApiResponse> UpdateFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Scheduled Task ID (from {functionId}/tasks)")] string taskConfigurationId,
			[FromQuery(Description = "The new Schedule: a cron expression with 5 fields, or 6 with seconds")] string schedule,
			[FromQuery(Description = "A new description (unchanged when empty)")] string description = null
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(taskConfigurationId) && !string.IsNullOrEmpty(schedule))
			{
				var scheduleError = ValidateSchedule(schedule);
				if (scheduleError != null) return BadRequest(scheduleError);

				var taskConfiguration = await Client.Api.GetJson<FunctionTask>(Url.Combine(_defaultScheduledTasksRoute, taskConfigurationId));
				if (taskConfiguration != null && string.Equals(taskConfiguration.FunctionId, functionId, StringComparison.OrdinalIgnoreCase))
				{
					taskConfiguration.Schedule = schedule.Trim();
					if (description != null) taskConfiguration.Description = description;

					var apiResponse = await Client.Api.Post(_defaultScheduledTasksRoute, taskConfiguration);
					if (apiResponse.Success)
					{
						return Ok(taskConfiguration);
					}
					else
					{
						return InternalError(apiResponse.GetContentUtf8String());
					}
				}
				else
				{
					return NotFound();
				}
			}
			else
			{
				return BadRequest();
			}
		}

		/// <summary>
		/// Returns an error message when the Schedule isn't a valid CRON expression (5 fields, or 6 with seconds)
		/// </summary>
		private static string ValidateSchedule(string schedule)
		{
			if (string.IsNullOrWhiteSpace(schedule)) return "Schedule is required";

			return ParseSchedule(schedule, out var error) == null ? $"Invalid Schedule : {error}" : null;
		}

		/// <summary>
		/// Edits a Scheduled Task : its Description, Schedule, Time Zone, Run Missed, and Job are replaced with the ones in the Body
		/// (an empty Job runs the Function without Parameters). The Enabled state is kept. POST (the Api Client can only send a Request Body with POST)
		/// </summary>
		[ApiPost("{functionId}/tasks/{taskConfigurationId}", Description = "Edits a Scheduled Task: its description, schedule, time zone, runMissed and Job are replaced with the ones in the body (no Job runs the Function without parameters). Whether it is enabled is kept.")]
		public async Task<TrakHoundApiResponse> EditFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Scheduled Task ID (from {functionId}/tasks)")] string taskConfigurationId,
			[FromBody(ContentType = "application/json", Description = "The Scheduled Task's new description, schedule, timeZone, runMissed and jobId")] FunctionTask edit
			)
		{
			if (string.IsNullOrEmpty(InstanceId) || string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(taskConfigurationId) || edit == null) return BadRequest();

			var scheduleError = ValidateSchedule(edit.Schedule);
			if (scheduleError != null) return BadRequest(scheduleError);

			if (GetTimeZone(edit.TimeZone, out var timeZoneError) == null) return BadRequest(timeZoneError);

			if (!string.IsNullOrEmpty(edit.JobId))
			{
				var jobResponse = await ReadJob(functionId, edit.JobId);
				if (jobResponse.Error != null) return InternalError(jobResponse.Error);
				if (jobResponse.Job == null) return BadRequest($"Job Not Found : {edit.JobId}");
			}

			var taskConfiguration = await Client.Api.GetJson<FunctionTask>(Url.Combine(_defaultScheduledTasksRoute, taskConfigurationId));
			if (taskConfiguration == null || !string.Equals(taskConfiguration.FunctionId, functionId, StringComparison.OrdinalIgnoreCase)) return NotFound();

			taskConfiguration.Description = !string.IsNullOrWhiteSpace(edit.Description) ? edit.Description.Trim() : null;
			taskConfiguration.Schedule = edit.Schedule.Trim();
			taskConfiguration.TimeZone = !string.IsNullOrWhiteSpace(edit.TimeZone) ? edit.TimeZone.Trim() : null;
			taskConfiguration.RunMissed = edit.RunMissed;
			taskConfiguration.JobId = !string.IsNullOrEmpty(edit.JobId) ? edit.JobId : null;

			var apiResponse = await Client.Api.Post(_defaultScheduledTasksRoute, taskConfiguration);
			return apiResponse.Success ? Ok(taskConfiguration) : InternalError(apiResponse.GetContentUtf8String());
		}

		[ApiPut("{functionId}/tasks/{taskConfigurationId}/enable", Description = "Enables a Scheduled Task so it runs on its schedule.")]
		public async Task<TrakHoundApiResponse> EnableFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Scheduled Task ID (from {functionId}/tasks)")] string taskConfigurationId
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(taskConfigurationId))
			{
                var taskConfiguration = await Client.Api.GetJson<FunctionTask>(Url.Combine(_defaultScheduledTasksRoute, taskConfigurationId));
                if (taskConfiguration != null)
                {
                    taskConfiguration.Enabled = true;

                    var apiResponse = await Client.Api.Post(_defaultScheduledTasksRoute, taskConfiguration);
                    if (apiResponse.Success)
                    {
                        return Ok(taskConfiguration);
                    }
                    else
                    {
                        return InternalError(apiResponse.GetContentUtf8String());
                    }
                }
                else
                {
                    return NotFound();
                }
			}
			else
			{
				return BadRequest();
			}
		}

		[ApiPut("{functionId}/tasks/{taskConfigurationId}/disable", Description = "Disables a Scheduled Task so it stops running (it is kept and can be enabled again).")]
		public async Task<TrakHoundApiResponse> DisableFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Scheduled Task ID (from {functionId}/tasks)")] string taskConfigurationId
			)
		{
            if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(taskConfigurationId))
            {
                var taskConfiguration = await Client.Api.GetJson<FunctionTask>(Url.Combine(_defaultScheduledTasksRoute, taskConfigurationId));
                if (taskConfiguration != null)
                {
                    taskConfiguration.Enabled = false;

                    var apiResponse = await Client.Api.Post(_defaultScheduledTasksRoute, taskConfiguration);
                    if (apiResponse.Success)
                    {
                        return Ok(taskConfiguration);
                    }
                    else
                    {
                        return InternalError(apiResponse.GetContentUtf8String());
                    }
                }
                else
                {
                    return NotFound();
                }
            }
            else
            {
                return BadRequest();
            }
        }

		[ApiDelete("{functionId}/tasks/{taskConfigurationId}", Description = "Deletes a Scheduled Task.")]
		public async Task<TrakHoundApiResponse> DeleteFunctionTask(
			[FromRoute(Description = "The Function ID (from list)")] string functionId,
			[FromRoute(Description = "The Scheduled Task ID (from {functionId}/tasks)")] string taskConfigurationId
			)
		{
			if (!string.IsNullOrEmpty(InstanceId) && !string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(taskConfigurationId))
			{
                var apiResponse = await Client.Api.Delete(Url.Combine(_defaultScheduledTasksRoute, taskConfigurationId));
                if (apiResponse.Success)
				{
                    var query = $"delete from \"{FunctionTaskModel.SchemaName}\".\"{FunctionTaskModel.TableName}\" where \"instance_id\" = @instanceId and \"function_id\" = @functionId and \"task_id\" = @taskId;";

                    var parameters = new TrakHoundTableParameterCollection();
                    parameters.Add("instanceId", InstanceId);
                    parameters.Add("functionId", functionId);
                    parameters.Add("taskId", taskConfigurationId);

                    var tableResponse = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.AnsiWrite(query, parameters));
                    if (tableResponse.Success)
                    {
                        return Ok();
                    }
                    else
                    {
                        return InternalError(tableResponse.Message);
                    }
                }
				else
				{
					return InternalError(apiResponse.GetContentUtf8String());
				}
            }
			else
			{
				return BadRequest();
			}
		}
	}
}
