// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Workers
{
    /// <summary>
    /// How a Worker is started
    /// </summary>
    public enum TrakHoundWorkerStatupType
    {
        /// <summary>
        /// The Worker is only started when requested
        /// </summary>
        Manual,
        /// <summary>
        /// The Worker is started automatically
        /// </summary>
        Automatic
    }
}
