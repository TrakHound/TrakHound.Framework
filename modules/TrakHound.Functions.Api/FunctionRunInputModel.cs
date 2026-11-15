// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// An Input Parameter of a Function Run (one row per Parameter, after a Job's Parameters are applied, written when the Run completes)
    /// </summary>
    [Table(TableName, SchemaName)]
    [TableIndex("ix_function_run_input_instance_function_run", "instance_id", "function_id", "run_id")] // The Inputs of a Run (also used by Retention)
    public class FunctionRunInputModel
    {
        public const string TableName = "function_run_input";
        public const string SchemaName = "trakhound";


        [Column]
        [NotNull]
        public string RunId { get; set; }

        [Column]
        [NotNull]
        public string InstanceId { get; set; }

        [Column]
        [NotNull]
        public string FunctionId { get; set; }

        [Column]
        [NotNull]
        public string Name { get; set; }

        // Values can be long (ex. JSON), and a value that doesn't fit would fail the whole Run's insert
        [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
        public string Value { get; set; }
    }
}
