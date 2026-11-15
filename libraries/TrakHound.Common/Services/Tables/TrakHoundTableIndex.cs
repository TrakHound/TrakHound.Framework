// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// An Index on a Table
    /// </summary>
    public class TrakHoundTableIndex
    {
        /// <summary>
        /// The name of the Index (unique within the Table)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Whether the Index is Clustered or NonClustered
        /// </summary>
        public TrakHoundTableIndexType Type { get; set; }

        /// <summary>
        /// True if no two rows can have the same values in the Columns of the Index
        /// </summary>
        public bool IsUnique { get; set; }

        /// <summary>
        /// The Columns included in the Index (in order)
        /// </summary>
        public TrakHoundTableIndexColumn[] Columns { get; set; }

        /// <summary>
        /// The names of the Columns stored in the Index without being part of its key (null if none)
        /// </summary>
        public string[] IncludeColumns { get; set; }
    }
}
