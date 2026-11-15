// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Pages.Explorer.Functions
{
    /// <summary>
    /// A snapshot of the Scheduled Tasks Worker, from the TrakHound.ScheduledTasks.Api "status" route
    /// </summary>
    public class WorkerStatus
    {
        /// <summary>
        /// When this Api received the snapshot
        /// </summary>
        [JsonPropertyName("received")]
        public DateTime Received { get; set; }

        /// <summary>
        /// The Worker hasn't reported recently (it may be stopped)
        /// </summary>
        [JsonPropertyName("stale")]
        public bool Stale { get; set; }

        [JsonPropertyName("workerId")]
        public string WorkerId { get; set; }

        [JsonPropertyName("instanceId")]
        public string InstanceId { get; set; }

        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }

        [JsonPropertyName("overlapPolicy")]
        public string OverlapPolicy { get; set; }

        [JsonPropertyName("maxConcurrentRuns")]
        public int MaxConcurrentRuns { get; set; }

        [JsonPropertyName("maxConcurrentRunsPerTask")]
        public int MaxConcurrentRunsPerTask { get; set; }

        /// <summary>
        /// Runs in progress (Run Slots in use)
        /// </summary>
        [JsonPropertyName("runningCount")]
        public int RunningCount { get; set; }

        /// <summary>
        /// Runs waiting for a Run Slot or for their Task's previous Run
        /// </summary>
        [JsonPropertyName("waitingCount")]
        public int WaitingCount { get; set; }

        [JsonPropertyName("tasks")]
        public List<ScheduledTaskStatus> Tasks { get; set; } = new List<ScheduledTaskStatus>();

        /// <summary>
        /// The next occurrences across all Tasks, in time order
        /// </summary>
        [JsonPropertyName("upcoming")]
        public List<UpcomingRun> Upcoming { get; set; } = new List<UpcomingRun>();

        /// <summary>
        /// Recent events (newest first)
        /// </summary>
        [JsonPropertyName("events")]
        public List<StatusEvent> Events { get; set; } = new List<StatusEvent>();
    }

    public static class TaskStates
    {
        public const string Scheduled = "scheduled";
        public const string Waiting = "waiting";
        public const string Running = "running";
        public const string Ended = "ended";
        public const string Invalid = "invalid";
    }

    public static class StatusEventTypes
    {
        public const string Due = "due";
        public const string Started = "started";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        public const string Dropped = "dropped";
        public const string Missed = "missed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>
    /// A Scheduled Task's state and its counts since the Worker started
    /// </summary>
    public class ScheduledTaskStatus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("jobId")]
        public string JobId { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("schedule")]
        public string Schedule { get; set; }

        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; }

        /// <summary>
        /// scheduled | waiting | running | ended | invalid
        /// </summary>
        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("nextRun")]
        public DateTime? NextRun { get; set; }

        [JsonPropertyName("lastDue")]
        public DateTime? LastDue { get; set; }

        [JsonPropertyName("running")]
        public int Running { get; set; }

        [JsonPropertyName("waiting")]
        public int Waiting { get; set; }

        /// <summary>
        /// When the oldest Run in progress started
        /// </summary>
        [JsonPropertyName("runStarted")]
        public DateTime? RunStarted { get; set; }

        /// <summary>
        /// When the oldest waiting Run was queued
        /// </summary>
        [JsonPropertyName("waitingSince")]
        public DateTime? WaitingSince { get; set; }

        [JsonPropertyName("dueCount")]
        public int DueCount { get; set; }

        [JsonPropertyName("startedCount")]
        public int StartedCount { get; set; }

        [JsonPropertyName("completedCount")]
        public int CompletedCount { get; set; }

        [JsonPropertyName("failedCount")]
        public int FailedCount { get; set; }

        [JsonPropertyName("skippedCount")]
        public int SkippedCount { get; set; }

        [JsonPropertyName("droppedCount")]
        public int DroppedCount { get; set; }

        [JsonPropertyName("missedRunCount")]
        public int MissedRunCount { get; set; }

        [JsonPropertyName("lastFinished")]
        public DateTime? LastFinished { get; set; }

        [JsonPropertyName("lastStatusCode")]
        public int? LastStatusCode { get; set; }

        [JsonPropertyName("lastSuccess")]
        public bool? LastSuccess { get; set; }

        [JsonPropertyName("lastDurationMs")]
        public double? LastDurationMs { get; set; }

        [JsonPropertyName("lastMessage")]
        public string LastMessage { get; set; }

    }

    public class UpcomingRun
    {
        [JsonPropertyName("time")]
        public DateTime Time { get; set; }

        [JsonPropertyName("taskId")]
        public string TaskId { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    public class StatusEvent
    {
        [JsonPropertyName("time")]
        public DateTime Time { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("taskId")]
        public string TaskId { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
