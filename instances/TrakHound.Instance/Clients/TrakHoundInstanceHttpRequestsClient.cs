// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Instances;

namespace TrakHound.Clients
{
    /// <summary>
    /// Reads the HTTP Metrics and Recent Requests directly from the Instance running in this process
    /// </summary>
    public class TrakHoundInstanceHttpRequestsClient : ITrakHoundHttpRequestsClient
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundInstanceHttpRequestsClient(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public Task<TrakHoundHttpMetrics> GetMetrics()
        {
            return Task.FromResult(_instance?.HttpMetrics?.GetMetrics());
        }

        public Task<TrakHoundHttpRecentRequests> GetRecentRequests()
        {
            return Task.FromResult(_instance?.HttpRequests?.Get());
        }
    }
}
