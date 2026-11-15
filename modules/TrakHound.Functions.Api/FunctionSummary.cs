// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// Run counts and durations for a set of Function Runs (durations are in milliseconds)
    /// </summary>
    public class FunctionRunSummary
    {
        [JsonPropertyName("runCount")]
        public int RunCount { get; set; }

        [JsonPropertyName("successCount")]
        public int SuccessCount { get; set; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }

        [JsonPropertyName("cancelledCount")]
        public int CancelledCount { get; set; }

        [JsonPropertyName("averageDuration")]
        public double AverageDuration { get; set; }

        [JsonPropertyName("maxDuration")]
        public double MaxDuration { get; set; }

        [JsonPropertyName("lastRun")]
        public DateTime? LastRun { get; set; }

        [JsonPropertyName("lastRunId")]
        public string LastRunId { get; set; }

        [JsonPropertyName("lastStatusCode")]
        public int? LastStatusCode { get; set; }

        [JsonPropertyName("lastStatus")]
        public string LastStatus { get; set; }


        /// <summary>
        /// Sets the counts and durations from a row of the summary query (no Runs when row is null), and the latest Run
        /// </summary>
        internal void SetRuns(FunctionSummaryRow row, DateTime? lastRun, string lastRunId, int? lastStatusCode, Func<int, string> getStatus)
        {
            if (row == null) return;

            RunCount = row.RunCount;
            CancelledCount = row.CancelledCount;
            SuccessCount = row.SuccessCount;
            ErrorCount = RunCount - SuccessCount - CancelledCount;

            if (RunCount > 0)
            {
                // Durations are nanoseconds, read as whole ticks (like a TimeSpan Column)
                AverageDuration = TimeSpan.FromTicks(row.TotalDuration / 100).TotalMilliseconds / RunCount;
                MaxDuration = TimeSpan.FromTicks(row.MaxDuration / 100).TotalMilliseconds;

                LastRun = lastRun.HasValue ? DateTime.SpecifyKind(lastRun.Value, DateTimeKind.Utc) : null;
                LastRunId = lastRunId;
                LastStatusCode = lastStatusCode;
                LastStatus = lastStatusCode.HasValue ? getStatus(lastStatusCode.Value) : null;
            }
        }
    }

    /// <summary>
    /// Runs of a Function within a time window, by Trigger and by Scheduled Task
    /// </summary>
    public class FunctionSummary : FunctionRunSummary
    {
        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        [JsonPropertyName("scheduledRunCount")]
        public int ScheduledRunCount { get; set; }

        [JsonPropertyName("manualRunCount")]
        public int ManualRunCount { get; set; }

        [JsonPropertyName("apiRunCount")]
        public int ApiRunCount { get; set; }

        /// <summary>
        /// The next Run of any enabled Scheduled Task
        /// </summary>
        [JsonPropertyName("nextRun")]
        public DateTime? NextRun { get; set; }

        [JsonPropertyName("tasks")]
        public List<FunctionTaskSummary> Tasks { get; set; }

        [JsonPropertyName("jobs")]
        public List<FunctionJob> Jobs { get; set; }

        [JsonPropertyName("timeline")]
        public List<FunctionTimelineBucket> Timeline { get; set; }
    }

    /// <summary>
    /// Runs started by a Scheduled Task within a time window
    /// </summary>
    public class FunctionTaskSummary : FunctionRunSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("schedule")]
        public string Schedule { get; set; }

        /// <summary>
        /// Set when the Schedule is not a valid CRON expression
        /// </summary>
        [JsonPropertyName("scheduleError")]
        public string ScheduleError { get; set; }

        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; }

        [JsonPropertyName("jobId")]
        public string JobId { get; set; }

        [JsonPropertyName("jobName")]
        public string JobName { get; set; }

        [JsonPropertyName("runMissed")]
        public bool RunMissed { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; }

        /// <summary>
        /// Seconds between the next two occurrences of the Schedule
        /// </summary>
        [JsonPropertyName("intervalSeconds")]
        public double? IntervalSeconds { get; set; }

        [JsonPropertyName("nextRun")]
        public DateTime? NextRun { get; set; }

        /// <summary>
        /// Schedule occurrences since the first Run in the window (null when there are no Runs to compare against)
        /// </summary>
        [JsonPropertyName("expectedRunCount")]
        public int? ExpectedRunCount { get; set; }

        /// <summary>
        /// Schedule occurrences without a Run (ex. skipped while the previous Run was still in progress, or the Instance was stopped)
        /// </summary>
        [JsonPropertyName("missedRunCount")]
        public int? MissedRunCount { get; set; }
    }

    public class FunctionTimelineBucket
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("runCount")]
        public int RunCount { get; set; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }
    }
}
