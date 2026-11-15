// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// The Package version a Function Run ran, the Job it used and the CPU it consumed (written when the Run completes).
    /// Kept in its own table so existing function_run tables don't need new columns
    /// </summary>
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    [TableIndex("ix_function_run_detail_instance_start", "instance_id", "start")] // Retention
    public class FunctionRunDetailModel
    {
        public const string TableName = "function_run_detail";
        public const string SchemaName = "trakhound";


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
        public string PackageVersion { get; set; }

        [Column]
        public string JobId { get; set; }

        /// <summary>
        /// CPU time in milliseconds (0 when CPU Monitoring is disabled)
        /// </summary>
        [Column]
        public double CpuTime { get; set; }

        /// <summary>
        /// CPU usage over the Run (percent of total machine capacity, 0 - 100)
        /// </summary>
        [Column]
        public double CpuUsage { get; set; }

        [Column]
        [NotNull]
        public DateTime Start { get; set; }
    }
}
