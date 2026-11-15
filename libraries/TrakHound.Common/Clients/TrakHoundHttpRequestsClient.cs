// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundHttpRequestsClient that queries the HTTP Metrics and Recent Requests of a remote Instance
    /// </summary>
    public class TrakHoundHttpRequestsClient : TrakHoundHttpClientBase, ITrakHoundHttpRequestsClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.HttpPrefix;


        /// <summary>
        /// Creates a new HTTP Requests Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpRequestsClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundHttpMetrics> GetMetrics()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "metrics");
            return await Requests.Get<TrakHoundHttpMetrics>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundHttpRecentRequests> GetRecentRequests()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "requests");
            return await Requests.Get<TrakHoundHttpRecentRequests>(url);
        }
    }
}
