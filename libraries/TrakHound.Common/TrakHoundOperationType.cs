// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// Determines the type of Operation
    /// </summary>
    public enum TrakHoundOperationType
    {
        /// <summary>
        /// Deletes the data
        /// </summary>
        Delete = -1,
        /// <summary>
        /// Empties the data
        /// </summary>
        Empty = 0,
        /// <summary>
        /// Publishes the data
        /// </summary>
        Publish = 1
    }
}
