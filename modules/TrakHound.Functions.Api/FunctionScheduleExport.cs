// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TrakHound.ScheduledTasks;

namespace TrakHound.Functions
{
    /// <summary>
    /// The Jobs and Scheduled Tasks of Functions, to copy to another Instance (or written offline) and import.
    /// Tasks reference Jobs by (functionId, jobId), which are the same on every Instance
    /// </summary>
    public class FunctionScheduleExport
    {
        public const string FormatName = "trakhound.functions.schedules";
        public const int CurrentVersion = 1;


        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("exported")]
        public DateTime? Exported { get; set; }

        /// <summary>
        /// The Instance the Jobs and Tasks were exported from (informational)
        /// </summary>
        [JsonPropertyName("sourceInstanceId")]
        public string SourceInstanceId { get; set; }

        [JsonPropertyName("jobs")]
        public List<FunctionJob> Jobs { get; set; }

        [JsonPropertyName("tasks")]
        public List<ScheduledTaskConfiguration> Tasks { get; set; }
    }

    public class FunctionScheduleImportResult
    {
        /// <summary>
        /// True when every Job and Task is valid and there are no Conflicts (and, when not a Dry Run, everything was written)
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; }

        /// <summary>
        /// An error that isn't about one Job or Task
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("jobs")]
        public List<FunctionJobImportItem> Jobs { get; set; } = new List<FunctionJobImportItem>();

        /// <summary>
        /// The Result of the Scheduled Tasks Api Import (with the Tasks whose Job wasn't found marked invalid)
        /// </summary>
        [JsonPropertyName("tasks")]
        public ScheduledTaskImportResult Tasks { get; set; }
    }

    public class FunctionJobImportItem
    {
        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// create, update, unchanged, skip, conflict, invalid, or error
        /// </summary>
        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("messages")]
        public List<string> Messages { get; set; }

        /// <summary>
        /// The Job that is written (Create and Update)
        /// </summary>
        [JsonIgnore]
        public FunctionJob Job { get; set; }
    }
}
