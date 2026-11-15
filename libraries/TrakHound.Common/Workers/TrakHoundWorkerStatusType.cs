// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Workers
{
    /// <summary>
    /// The Status of a Worker
    /// </summary>
    public enum TrakHoundWorkerStatusType
    {
        /// <summary>
        /// The Worker encountered an Error (the Worker Engine restarts a running Worker that reports Error)
        /// </summary>
        Error,
        /// <summary>
        /// The Worker is stopped
        /// </summary>
        Stopped,
        /// <summary>
        /// The Worker is stopping
        /// </summary>
        Stopping,
        /// <summary>
        /// The Worker is starting
        /// </summary>
        Starting,
        /// <summary>
        /// The Worker is started and running
        /// </summary>
        Started
    }
}
