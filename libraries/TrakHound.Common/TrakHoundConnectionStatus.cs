// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The status of a connection
    /// </summary>
    public struct TrakHoundConnectionStatus
    {
        /// <summary>
        /// Returns true if the connection is currently established
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// A message describing the status (ex. an error message when not connected)
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The time the status was recorded as Unix time (in nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }
    }
}
