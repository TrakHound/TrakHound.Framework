// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The definition of a Table, independent of any database. It is sent to a Table Service, which creates the Table
    /// and writes its rows in the way its database requires. See TrakHoundTables.GetDefinition
    /// </summary>
    public class TrakHoundTableDefinition
    {
        /// <summary>
        /// The name of the Table
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Schema of the Table (null uses the database default)
        /// </summary>
        [JsonPropertyName("schema")]
        public string Schema { get; set; }

        /// <summary>
        /// True if an Insert skips rows whose Primary Key already exists instead of failing
        /// </summary>
        [JsonPropertyName("ignoreDuplicateKey")]
        public bool IgnoreDuplicateKey { get; set; }

        /// <summary>
        /// The Columns of the Table (in order)
        /// </summary>
        [JsonPropertyName("columns")]
        public TrakHoundTableColumnDefinition[] Columns { get; set; }

        /// <summary>
        /// The Indexes of the Table (null if none)
        /// </summary>
        [JsonPropertyName("indexes")]
        public TrakHoundTableIndex[] Indexes { get; set; }
    }
}
