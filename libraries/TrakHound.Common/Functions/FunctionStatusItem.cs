// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Functions
{
    /// <summary>
    /// A Status change of a Function Run along with the time it occurred
    /// </summary>
    public struct TrakHoundStatusItem
    {
        /// <summary>
        /// The Status the Function Run changed to
        /// </summary>
        public TrakHoundFunctionStatusType StatusType { get; set; }

        /// <summary>
        /// The time the Status occurred (Unix Nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }


        /// <summary>
        /// Create a new Status Item with the specified Status and Timestamp (Unix Nanoseconds)
        /// </summary>
        public TrakHoundStatusItem(TrakHoundFunctionStatusType statusType, long timestamp)
        {
            StatusType = statusType;
            Timestamp = timestamp;
        }
    }
}
