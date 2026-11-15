// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Functions
{
    /// <summary>
    /// A Status update for a Function Run
    /// </summary>
    public struct TrakHoundFunctionStatusResponse
    {
        /// <summary>
        /// The Status of the Function Run
        /// </summary>
        public TrakHoundFunctionStatusType Status { get; set; }

        /// <summary>
        /// The time the Status occurred (Unix Nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }
    }
}
