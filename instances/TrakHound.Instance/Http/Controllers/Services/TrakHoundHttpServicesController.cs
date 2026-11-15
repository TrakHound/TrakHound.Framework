// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.ServicesPrefix)]
    [Produces("application/json")]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Services)]
    public class TrakHoundHttpServicesController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpServicesController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet]
        [RouteDescription("Lists the Services that are running.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Services.TrakHoundServiceInformation>))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetServices()
        {
            using var client = GetClient();
            if (client != null)
            {
                var informations = await client.Services.Drivers.GetDrivers();
                if (informations != null)
                {
                    return Ok(informations);
                }
            }

            return NotFound();
        }

        [HttpGet("metrics")]
        [RouteDescription("Gets the Metrics of every Service.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Services.TrakHoundServiceMetrics>))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetMetrics()
        {
            using var client = GetClient();
            if (client != null)
            {
                return Ok(await client.Services.Drivers.GetMetrics() ?? Enumerable.Empty<TrakHound.Services.TrakHoundServiceMetrics>());
            }

            return NotFound();
        }

        [HttpGet("metrics/{configurationId}")]
        [RouteDescription("Gets the Metrics of a Service.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Configuration")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceMetrics))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetMetrics([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client != null)
            {
                var metrics = await client.Services.Drivers.GetMetrics(configurationId);
                if (metrics != null)
                {
                    return Ok(metrics);
                }
            }

            return NotFound();
        }

        [HttpGet("{configurationId}")]
        [RouteDescription("Gets a Service that is running.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Configuration")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceInformation))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetService([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client != null)
            {
                var information = await client.Services.Drivers.GetDriver(configurationId);
                if (information != null)
                {
                    return Ok(information);
                }
            }

            return NotFound();
        }
    }
}
