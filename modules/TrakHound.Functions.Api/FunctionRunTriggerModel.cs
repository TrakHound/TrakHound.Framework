// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// What started a Function Run (a Scheduled Task, the Admin Run page or an API request).
    /// Kept in its own table so existing function_run tables don't need a new column
    /// </summary>
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    [TableIndex("ix_function_run_trigger_instance_start", "instance_id", "start", Include = new[] { "task_id", "trigger" })] // Triggers by time range
    [TableIndex("ix_function_run_trigger_instance_task_start", "instance_id", "task_id", "start")] // The last Run of a Scheduled Task (max(start) by task_id)
    public class FunctionRunTriggerModel
    {
        public const string TableName = "function_run_trigger";
        public const string SchemaName = "trakhound";

        public const string Scheduled = "scheduled";
        public const string Manual = "manual";
        public const string Api = "api";


        [Column]
        [PrimaryKey]
        [NotNull]
        public string InstanceId { get; set; }

        [Column]
        [PrimaryKey]
        [NotNull]
        public string FunctionId { get; set; }

        [Column]
        [PrimaryKey]
        [NotNull]
        public string RunId { get; set; }

        [Column]
        [NotNull]
        public string Trigger { get; set; }

        [Column]
        public string TaskId { get; set; }

        [Column]
        [NotNull]
        public DateTime Start { get; set; }
    }
}
