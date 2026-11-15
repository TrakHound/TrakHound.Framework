// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    /// <summary>
    /// System API for reading the Metrics and the most recent of the HTTP Requests received by this Instance
    /// </summary>
    [ApiController]
    [Route(TrakHoundHttpConstants.HttpPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Metrics)]
    public class TrakHoundHttpRequestsController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpRequestsController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet("metrics")]
        [RouteDescription("Gets the Metrics for the HTTP Requests received by the Instance.")]
        [RouteResponse(200, typeof(TrakHoundHttpMetrics))]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.HttpMetrics?.GetMetrics() ?? new TrakHoundHttpMetrics());
        }

        [HttpGet("requests")]
        [RouteDescription("Gets the most recent HTTP Requests received by the Instance (newest first).")]
        [RouteResponse(200, typeof(TrakHoundHttpRecentRequests))]
        public IActionResult GetRecentRequests()
        {
            return Ok(_instance.HttpRequests?.Get() ?? new TrakHoundHttpRecentRequests());
        }
    }
}
