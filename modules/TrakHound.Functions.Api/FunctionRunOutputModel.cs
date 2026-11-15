// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// An Output Parameter of a Function Run (one row per Parameter, written when the Run completes)
    /// </summary>
    [Table(TableName, SchemaName)]
    [TableIndex("ix_function_run_output_instance_function_run", "instance_id", "function_id", "run_id")] // The Outputs of a Run (also used by Retention)
    public class FunctionRunOutputModel
    {
        public const string TableName = "function_run_output";
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

        [Column]
        public string Value { get; set; }
    }
}
