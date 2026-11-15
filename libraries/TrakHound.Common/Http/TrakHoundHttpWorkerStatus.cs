// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using TrakHound.Workers;

namespace TrakHound.Http
{
    /// <summary>
    /// JSON representation of a Worker Status sent over HTTP (the Status is sent as a string)
    /// </summary>
    public class TrakHoundHttpWorkerStatus
    {
        /// <summary>
        /// The Id of the Worker
        /// </summary>
        [JsonPropertyName("workerId")]
        public string WorkerId { get; set; }

        /// <summary>
        /// The Status of the Worker (the name of a TrakHoundWorkerStatusType value)
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// A message describing the Status (ex. an error message)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }


        /// <summary>
        /// Creates an empty Worker Status
        /// </summary>
        public TrakHoundHttpWorkerStatus() { }

        /// <summary>
        /// Creates a Worker Status from a TrakHoundWorkerStatus
        /// </summary>
        public TrakHoundHttpWorkerStatus(TrakHoundWorkerStatus status)
        {
            WorkerId = status.WorkerId;
            Status = status.Status.ToString();
            Message = status.Message;
        }

        /// <summary>
        /// Converts to a TrakHoundWorkerStatus
        /// </summary>
        public TrakHoundWorkerStatus ToStatus()
        {
            var status = new TrakHoundWorkerStatus();
            status.WorkerId = WorkerId;
            status.Status = Status.ConvertEnum<TrakHoundWorkerStatusType>();
            status.Message = Message;
            return status;
        }


        /// <summary>
        /// Converts a list of TrakHoundWorkerStatus to their HTTP representation (empty if the list is null or empty)
        /// </summary>
        public static IEnumerable<TrakHoundHttpWorkerStatus> Create(IEnumerable<TrakHoundWorkerStatus> statuses)
        {
            var httpStatuses = new List<TrakHoundHttpWorkerStatus>();
            if (!statuses.IsNullOrEmpty())
            {
                foreach (var status in statuses)
                {
                    httpStatuses.Add(new TrakHoundHttpWorkerStatus(status));
                }
            }
            return httpStatuses;
        }

        /// <summary>
        /// Converts a list of HTTP Worker Statuses back to TrakHoundWorkerStatus (empty if the list is null or empty)
        /// </summary>
        public static IEnumerable<TrakHoundWorkerStatus> ToStatuses(IEnumerable<TrakHoundHttpWorkerStatus> httpStatuses)
        {
            var statuses = new List<TrakHoundWorkerStatus>();
            if (!httpStatuses.IsNullOrEmpty())
            {
                foreach (var httpStatus in httpStatuses)
                {
                    statuses.Add(httpStatus.ToStatus());
                }
            }
            return statuses;
        }
    }
}
