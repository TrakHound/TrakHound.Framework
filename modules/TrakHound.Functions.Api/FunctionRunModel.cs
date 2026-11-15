// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    [TableIndex("ix_function_run_instance_start", "instance_id", "start", Include = new[] { "function_id", "status_code", "duration" })] // Runs by time range (the run status summaries)
    public class FunctionRunModel
    {
        public const string TableName = "function_run";
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
        public string Id { get; set; }

        [Column]
        [NotNull]
        public int StatusCode { get; set; }

        [Column]
        [NotNull]
        public DateTime Start { get; set; }

        [Column]
        public DateTime End { get; set; }

        [Column]
        public TimeSpan Duration { get; set; }
    }
}
