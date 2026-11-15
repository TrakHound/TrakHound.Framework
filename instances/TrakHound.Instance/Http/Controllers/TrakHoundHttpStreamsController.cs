// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    /// <summary>
    /// System API for reading the Metrics of this Instance's HTTP Stream connections (WebSockets and Server-Sent Events)
    /// </summary>
    [ApiController]
    [Route(TrakHoundHttpConstants.StreamsPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Metrics)]
    public class TrakHoundHttpStreamsController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpStreamsController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet("metrics")]
        [RouteDescription("Gets the Metrics for the Instance's HTTP Stream connections (WebSockets and Server-Sent Events).")]
        [RouteResponse(200, typeof(TrakHoundStreamMetrics))]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.StreamMetrics?.GetMetrics() ?? new TrakHoundStreamMetrics());
        }
    }
}
