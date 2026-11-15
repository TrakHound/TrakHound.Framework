// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The result of an Acknowledge request
    /// </summary>
    public enum TrakHoundAcknowledgeResultType
    {
        /// <summary>
        /// An error occurred while Acknowledging
        /// </summary>
        Error = -1,
        /// <summary>
        /// The delivery to Acknowledge was not found
        /// </summary>
        NotFound = 0,
        /// <summary>
        /// The delivery was Acknowledged successfully
        /// </summary>
        Success = 1
    }
}
