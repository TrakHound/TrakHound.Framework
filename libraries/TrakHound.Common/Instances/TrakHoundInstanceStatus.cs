// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instances
{
    /// <summary>
    /// The runtime Status of an Instance
    /// </summary>
    public enum TrakHoundInstanceStatus
    {
        /// <summary>
        /// The Instance encountered an error
        /// </summary>
        Error = -2,
        /// <summary>
        /// The Instance is stopping
        /// </summary>
        Stopping = -1,
        /// <summary>
        /// The Instance is stopped
        /// </summary>
        Stopped = 0,
        /// <summary>
        /// The Instance is initializing
        /// </summary>
        Initializing = 1,
        /// <summary>
        /// The Instance is initialized but not started
        /// </summary>
        Initialized = 2,
        /// <summary>
        /// The Instance is starting
        /// </summary>
        Starting = 3,
        /// <summary>
        /// The Instance is started and running
        /// </summary>
        Started = 4
    }
}
