// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Buffers;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundBuffersClient that queries the Buffers Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpBuffersClient : TrakHoundHttpClientBase, ITrakHoundBuffersClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.BuffersPrefix;


        /// <summary>
        /// Creates a new Buffers Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpBuffersClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute);
            return await Requests.Get<IEnumerable<TrakHoundBufferMetrics>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundBufferMetrics> GetMetrics(string bufferId)
        {
            if (string.IsNullOrEmpty(bufferId)) return null;

            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, bufferId);
            return await Requests.Get<TrakHoundBufferMetrics>(url);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics(IEnumerable<string> bufferIds)
        {
            if (bufferIds.IsNullOrEmpty()) return null;

            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute);
            return await Requests.Post<IEnumerable<TrakHoundBufferMetrics>>(url, (object)bufferIds);
        }
    }
}
