// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;
using TrakHound.Mcp;

namespace TrakHound.Http
{
    /// <summary>
    /// System API for reading the Metrics of this Instance's MCP Server (the MCP Server itself is mapped at /_mcp, see TrakHoundMcpServer)
    /// </summary>
    [ApiController]
    [Route(TrakHoundHttpConstants.McpPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Metrics)]
    public class TrakHoundHttpMcpController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpMcpController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet("metrics")]
        [RouteDescription("Gets the Metrics for the Instance's MCP Server.")]
        [RouteResponse(200, typeof(TrakHoundMcpMetrics))]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.McpMetrics?.GetMetrics() ?? new TrakHoundMcpMetrics());
        }
    }
}
