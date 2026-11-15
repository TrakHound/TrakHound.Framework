// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
	[Table(TableName, SchemaName)]
	[IgnoreDuplicateKey]
    public class FunctionTaskModel
	{
        public const string TableName = "function_task";
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
        public string TaskId { get; set; }
	}
}
