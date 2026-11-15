// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Pages.Explorer.Functions
{
    /// <summary>
    /// Run counts and durations (milliseconds) from the TrakHound.Functions.Api "functions/summary" route
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

        [JsonIgnore]
        public double SuccessRate => RunCount > 0 ? (double)SuccessCount / RunCount * 100 : 0;
    }

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

        [JsonPropertyName("nextRun")]
        public DateTime? NextRun { get; set; }

        [JsonPropertyName("tasks")]
        public List<FunctionTaskSummary> Tasks { get; set; }

        [JsonPropertyName("jobs")]
        public List<FunctionJob> Jobs { get; set; }

        [JsonPropertyName("timeline")]
        public List<FunctionTimelineBucket> Timeline { get; set; }

        [JsonIgnore]
        public int EnabledTaskCount => Tasks?.Count(o => o.Enabled) ?? 0;

        [JsonIgnore]
        public int TaskCount => Tasks?.Count ?? 0;

        [JsonIgnore]
        public int MissedRunCount => Tasks?.Sum(o => o.MissedRunCount ?? 0) ?? 0;

        /// <summary>
        /// Runs recorded before Triggers were tracked
        /// </summary>
        [JsonIgnore]
        public int UntrackedRunCount => Math.Max(0, RunCount - ScheduledRunCount - ManualRunCount - ApiRunCount);
    }

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

        [JsonPropertyName("expectedRunCount")]
        public int? ExpectedRunCount { get; set; }

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
