// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
	[Table(TableName, SchemaName)]
	[TableIndex("ix_function_log_instance_function_run", "instance_id", "function_id", "run_id")] // The Logs of a Run (no Primary Key: a Key column can't be added safely to existing Tables)
	public class FunctionLogModel
	{
		public const string TableName = "function_log";
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
        public int Level { get; set; }

        [Column]
        [NotNull]
        public string Message { get; set; }

		[Column]
        [NotNull]
        public DateTime Timestamp { get; set; }
	}
}
