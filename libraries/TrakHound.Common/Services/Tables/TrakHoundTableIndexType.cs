// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The type of a Table Index
    /// </summary>
    public enum TrakHoundTableIndexType
    {
        /// <summary>
        /// Determines the physical order of the rows in the Table (only one per Table)
        /// </summary>
        Clustered,
        /// <summary>
        /// Stored separately from the rows of the Table
        /// </summary>
        NonClustered
    }
}
