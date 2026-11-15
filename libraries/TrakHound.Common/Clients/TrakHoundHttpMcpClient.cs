// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Mcp;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundMcpClient that queries the MCP Metrics Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpMcpClient : TrakHoundHttpClientBase, ITrakHoundMcpClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.McpPrefix;


        /// <summary>
        /// Creates a new MCP Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpMcpClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundMcpMetrics> GetMetrics()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "metrics");
            return await Requests.Get<TrakHoundMcpMetrics>(url);
        }
    }
}
