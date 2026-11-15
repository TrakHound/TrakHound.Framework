// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundStreamsClient that queries the Streams Metrics Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpStreamsClient : TrakHoundHttpClientBase, ITrakHoundStreamsClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.StreamsPrefix;


        /// <summary>
        /// Creates a new Streams Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpStreamsClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundStreamMetrics> GetMetrics()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "metrics");
            return await Requests.Get<TrakHoundStreamMetrics>(url);
        }
    }
}
