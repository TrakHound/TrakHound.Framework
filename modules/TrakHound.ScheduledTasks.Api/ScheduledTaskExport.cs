// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// A set of Scheduled Task configurations to copy to another Instance (or written offline) and import
    /// </summary>
    public class ScheduledTaskExport
    {
        public const string FormatName = "trakhound.scheduled-tasks";
        public const int CurrentVersion = 1;


        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("exported")]
        public DateTime? Exported { get; set; }

        /// <summary>
        /// The Instance the Tasks were exported from (informational)
        /// </summary>
        [JsonPropertyName("sourceInstanceId")]
        public string SourceInstanceId { get; set; }

        [JsonPropertyName("tasks")]
        public List<ScheduledTaskConfiguration> Tasks { get; set; }
    }

    /// <summary>
    /// What an Import does when a Task (or Job) with the same ID already exists and is different
    /// </summary>
    public enum ScheduledTaskImportMode
    {
        /// <summary>
        /// Nothing is imported (the Result lists the Conflicts)
        /// </summary>
        Fail,

        /// <summary>
        /// The existing one is kept
        /// </summary>
        Skip,

        /// <summary>
        /// The existing one is replaced
        /// </summary>
        Overwrite
    }

    public static class ScheduledTaskImportAction
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Unchanged = "unchanged";
        public const string Skip = "skip";
        public const string Conflict = "conflict";
        public const string Invalid = "invalid";
        public const string Error = "error";
    }

    public class ScheduledTaskImportResult
    {
        /// <summary>
        /// True when every Task is valid and there are no Conflicts (and, when not a Dry Run, every Task was written)
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; }

        [JsonPropertyName("created")]
        public int Created { get; set; }

        [JsonPropertyName("updated")]
        public int Updated { get; set; }

        [JsonPropertyName("unchanged")]
        public int Unchanged { get; set; }

        [JsonPropertyName("skipped")]
        public int Skipped { get; set; }

        [JsonPropertyName("conflicts")]
        public int Conflicts { get; set; }

        [JsonPropertyName("invalid")]
        public int Invalid { get; set; }

        [JsonPropertyName("errors")]
        public int Errors { get; set; }

        [JsonPropertyName("tasks")]
        public List<ScheduledTaskImportItem> Tasks { get; set; } = new List<ScheduledTaskImportItem>();
    }

    public class ScheduledTaskImportItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        /// <summary>
        /// create, update, unchanged, skip, conflict, invalid, or error
        /// </summary>
        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("messages")]
        public List<string> Messages { get; set; }

        /// <summary>
        /// The configuration that is written (Create and Update)
        /// </summary>
        [JsonIgnore]
        public ScheduledTaskConfiguration Configuration { get; set; }

        /// <summary>
        /// The File the configuration is written to (Create and Update)
        /// </summary>
        [JsonIgnore]
        public string FileId { get; set; }
    }
}
