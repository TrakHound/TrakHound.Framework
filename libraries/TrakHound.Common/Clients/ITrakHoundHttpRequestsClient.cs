// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query the HTTP Request Metrics and Recent Requests of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundHttpRequestsClient
    {
        /// <summary>
        /// Gets the Metrics for the HTTP Requests received by the Instance
        /// </summary>
        Task<TrakHoundHttpMetrics> GetMetrics();

        /// <summary>
        /// Gets the most recent HTTP Requests received by the Instance (newest first)
        /// </summary>
        Task<TrakHoundHttpRecentRequests> GetRecentRequests();
    }
}
