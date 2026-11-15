// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// Determines how a value is updated
    /// </summary>
    public enum TrakHoundUpdateType
    {
        /// <summary>
        /// The value is replaced with the new value
        /// </summary>
        Absolute,
        /// <summary>
        /// The new value is added to the existing value
        /// </summary>
        Increment
    }
}
