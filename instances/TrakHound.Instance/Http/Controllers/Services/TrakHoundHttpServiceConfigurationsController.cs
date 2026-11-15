// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Services;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.ServiceConfigurationsPrefix)]
    [Produces("application/json")]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Services)]
    public class TrakHoundHttpServiceConfigurationsController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpServiceConfigurationsController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet("services")]
        [RouteDescription("Lists the Service Configurations.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundServiceConfiguration>))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetServices()
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            var configurations = await client.Services.Configurations.GetServices();
            return Ok(configurations ?? Enumerable.Empty<TrakHoundServiceConfiguration>());
        }

        [HttpGet("services/{configurationId}")]
        [RouteDescription("Gets a Service Configuration.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Configuration")]
        [RouteResponse(200, typeof(TrakHoundServiceConfiguration))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetService([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            var configuration = await client.Services.Configurations.GetService(configurationId);
            if (configuration != null) return Ok(configuration);

            return NotFound();
        }

        [HttpPut("services")]
        [TrakHoundSystemWrite]
        [RouteDescription("Saves a Service Configuration (adds it, or replaces the one with the same ID).")]
        [RouteParameterDescription("configuration", "The Service Configuration")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(404)]
        public async Task<IActionResult> SaveService([FromBody] TrakHoundServiceConfiguration configuration)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            if (await client.Services.Configurations.SaveService(configuration)) return Ok();

            return BadRequest();
        }

        [HttpDelete("services/{configurationId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Removes a Service Configuration.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Configuration")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> RemoveService([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            if (await client.Services.Configurations.RemoveService(configurationId)) return Ok();

            return NotFound();
        }


        [HttpGet("routers")]
        [RouteDescription("Lists the Service Router Configurations.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundServiceRouterConfiguration>))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetRouters()
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            var configurations = await client.Services.Configurations.GetRouters();
            return Ok(configurations ?? Enumerable.Empty<TrakHoundServiceRouterConfiguration>());
        }

        [HttpGet("routers/{configurationId}")]
        [RouteDescription("Gets a Service Router Configuration.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Router Configuration")]
        [RouteResponse(200, typeof(TrakHoundServiceRouterConfiguration))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetRouter([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            var configuration = await client.Services.Configurations.GetRouter(configurationId);
            if (configuration != null) return Ok(configuration);

            return NotFound();
        }

        [HttpPut("routers")]
        [TrakHoundSystemWrite]
        [RouteDescription("Saves a Service Router Configuration (adds it, or replaces the one with the same ID).")]
        [RouteParameterDescription("configuration", "The Service Router Configuration")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(404)]
        public async Task<IActionResult> SaveRouter([FromBody] TrakHoundServiceRouterConfiguration configuration)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            if (await client.Services.Configurations.SaveRouter(configuration)) return Ok();

            return BadRequest();
        }

        [HttpDelete("routers/{configurationId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Removes a Service Router Configuration.")]
        [RouteParameterDescription("configurationId", "The ID of the Service Router Configuration")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> RemoveRouter([FromRoute] string configurationId)
        {
            using var client = GetClient();
            if (client?.Services.Configurations == null) return NotFound();

            if (await client.Services.Configurations.RemoveRouter(configurationId)) return Ok();

            return NotFound();
        }
    }
}
