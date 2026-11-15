// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query the Stream connection Metrics of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundStreamsClient
    {
        /// <summary>
        /// Gets the Metrics for the Instance's HTTP Stream connections, WebSockets and Server-Sent Events (including the open connections)
        /// </summary>
        Task<TrakHoundStreamMetrics> GetMetrics();
    }
}
