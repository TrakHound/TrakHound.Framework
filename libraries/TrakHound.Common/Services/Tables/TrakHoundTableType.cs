// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The type of a database Table
    /// </summary>
    public enum TrakHoundTableType
    {
        /// <summary>
        /// A Table that stores rows
        /// </summary>
        Table,
        /// <summary>
        /// A View defined by a Query
        /// </summary>
        View
    }
}
