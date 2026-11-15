// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// Determines the mode that a Operation uses
    /// </summary>
    public enum TrakHoundOperationMode
    {
        /// <summary>
        /// The Operation is queued and processed in the background
        /// </summary>
        Async = 0,
        /// <summary>
        /// The Operation is processed before the call returns
        /// </summary>
        Sync = 1
    }
}
