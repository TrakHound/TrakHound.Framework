// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// One step of a Table Request. Only the properties used by its Type are set
    /// </summary>
    public class TrakHoundTableStep
    {
        /// <summary>
        /// What the step does
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTableStepType>))]
        public TrakHoundTableStepType Type { get; set; }

        /// <summary>
        /// Query : The SQL to run for each Dialect
        /// </summary>
        [JsonPropertyName("variants")]
        public TrakHoundTableQueryVariants Variants { get; set; }

        /// <summary>
        /// Query : The number of rows to skip (the Query should have an order by)
        /// </summary>
        [JsonPropertyName("skip")]
        public int? Skip { get; set; }

        /// <summary>
        /// Query : The most rows to return (the Query should have an order by)
        /// </summary>
        [JsonPropertyName("take")]
        public int? Take { get; set; }

        /// <summary>
        /// EnsureTable, Insert and Merge : The Table
        /// </summary>
        [JsonPropertyName("definition")]
        public TrakHoundTableDefinition Definition { get; set; }

        /// <summary>
        /// Insert and Merge : The rows, with a value for each Column of the Definition (in order)
        /// </summary>
        [JsonPropertyName("rows")]
        [JsonConverter(typeof(TrakHoundTableRowsJsonConverter))]
        public object[][] Rows { get; set; }

        /// <summary>
        /// Insert : True if existing rows with the same Primary Key are deleted first
        /// </summary>
        [JsonPropertyName("overwrite")]
        public bool Overwrite { get; set; }

        /// <summary>
        /// DeleteBatched : The Schema of the Table (null uses the database default)
        /// </summary>
        [JsonPropertyName("schema")]
        public string Schema { get; set; }

        /// <summary>
        /// DeleteBatched : The name of the Table
        /// </summary>
        [JsonPropertyName("table")]
        public string Table { get; set; }

        /// <summary>
        /// DeleteBatched : The condition, in the "ansi" subset of SQL, that selects the rows to delete. It uses "target" as the name of the Table
        /// </summary>
        [JsonPropertyName("where")]
        public string Where { get; set; }

        /// <summary>
        /// DeleteBatched : The most rows to delete in each batch
        /// </summary>
        [JsonPropertyName("batchSize")]
        public int BatchSize { get; set; }
    }
}
