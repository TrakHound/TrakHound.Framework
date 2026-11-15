// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Mcp;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query the MCP Server (Model Context Protocol) Metrics of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundMcpClient
    {
        /// <summary>
        /// Gets the Metrics for the Instance's MCP Server (Tool calls and Clients)
        /// </summary>
        Task<TrakHoundMcpMetrics> GetMetrics();
    }
}
