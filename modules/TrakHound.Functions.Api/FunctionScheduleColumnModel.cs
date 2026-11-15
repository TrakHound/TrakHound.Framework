// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// A Schedule column added on the Admin Schedule Board
    /// </summary>
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    public class FunctionScheduleColumnModel
    {
        public const string TableName = "function_schedule_column";
        public const string SchemaName = "trakhound";


        [Column]
        [PrimaryKey]
        [NotNull]
        public string InstanceId { get; set; }

        [Column]
        [PrimaryKey]
        [NotNull]
        public string Schedule { get; set; }

        [Column]
        public string Title { get; set; }
    }

    public class FunctionScheduleColumn
    {
        [JsonPropertyName("schedule")]
        public string Schedule { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// Seconds between the next two occurrences
        /// </summary>
        [JsonPropertyName("intervalSeconds")]
        public double? IntervalSeconds { get; set; }
    }
}
