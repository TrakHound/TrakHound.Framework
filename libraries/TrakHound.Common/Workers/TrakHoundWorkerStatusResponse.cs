// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Workers
{
    /// <summary>
    /// A Worker Status sent to Status subscribers
    /// </summary>
    public struct TrakHoundWorkerStatusResponse
    {
        /// <summary>
        /// The Identifier of the Worker
        /// </summary>
        public string WorkerId { get; set; }

        /// <summary>
        /// The Status of the Worker
        /// </summary>
        public TrakHoundWorkerStatusType Status { get; set; }

        /// <summary>
        /// When the Status was received (Unix Nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }
    }
}
