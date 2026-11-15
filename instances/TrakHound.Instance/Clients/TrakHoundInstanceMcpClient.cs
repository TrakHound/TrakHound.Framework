// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Instances;
using TrakHound.Mcp;

namespace TrakHound.Clients
{
    /// <summary>
    /// Reads the MCP Server Metrics directly from the Instance running in this process
    /// </summary>
    public class TrakHoundInstanceMcpClient : ITrakHoundMcpClient
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundInstanceMcpClient(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public Task<TrakHoundMcpMetrics> GetMetrics()
        {
            return Task.FromResult(_instance?.McpMetrics?.GetMetrics());
        }
    }
}
