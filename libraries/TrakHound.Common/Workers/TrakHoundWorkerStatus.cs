// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Workers
{
    /// <summary>
    /// A Status reported by a Worker
    /// </summary>
    public struct TrakHoundWorkerStatus
    {
        /// <summary>
        /// The Identifier of the Worker
        /// </summary>
        public string WorkerId { get; set; } 

        /// <summary>
        /// The reported Status
        /// </summary>
        public TrakHoundWorkerStatusType Status { get; set; } 

        /// <summary>
        /// An optional message describing the Status (ex. an Error message)
        /// </summary>
        public string Message { get; set; }


        /// <summary>
        /// Creates a Worker Status
        /// </summary>
        public TrakHoundWorkerStatus(string workerId, TrakHoundWorkerStatusType status, string message = null)
        {
            WorkerId = workerId;
            Status = status;
            Message = message;
        }
    }
}
