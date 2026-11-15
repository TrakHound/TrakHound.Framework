// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// What a step of a Table Request does
    /// </summary>
    public enum TrakHoundTableStepType
    {
        /// <summary>
        /// Runs SQL written for one or more Dialects
        /// </summary>
        Query,
        /// <summary>
        /// Creates a Table and its Indexes if they don't exist
        /// </summary>
        EnsureTable,
        /// <summary>
        /// Writes rows to a Table
        /// </summary>
        Insert,
        /// <summary>
        /// Updates existing rows of a Table (matched by Primary Key) and inserts new rows
        /// </summary>
        Merge,
        /// <summary>
        /// Deletes rows in batches until none match. Can't be used in a Transaction
        /// </summary>
        DeleteBatched
    }
}
