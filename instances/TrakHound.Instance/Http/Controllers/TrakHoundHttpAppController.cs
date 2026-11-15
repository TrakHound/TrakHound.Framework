// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Apps)]
    public class TrakHoundHttpAppController : ControllerBase
    {
        private readonly ITrakHoundInstance _server;


        public TrakHoundHttpAppController(ITrakHoundInstance server)
        {
            _server = server;
        }


        [HttpGet("_apps/information")]
        [RouteDescription("Lists the information for every App.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Apps.TrakHoundAppInformation>))]
        [RouteResponse(404)]
        public IActionResult GetInformations()
        {
            var information = _server.AppProvider.GetInformation();
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpGet("_apps/information/{appId}")]
        [RouteDescription("Gets the information for an App.")]
        [RouteParameterDescription("appId", "The ID of the App")]
        [RouteResponse(200, typeof(TrakHound.Apps.TrakHoundAppInformation))]
        [RouteResponse(404)]
        public IActionResult GetInformation(string appId)
        {
            var information = _server.AppProvider.GetInformation(appId);
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpPost("_apps/reload/{appId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Reloads the Configuration of an App.")]
        [RouteParameterDescription("appId", "The ID of the App")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> ReloadConfiguration(string appId)
        {
            if (await GetClient().Apps.ReloadConfiguration(appId))
            {
                return Ok();
            }

            return NotFound();
        }

        [HttpPost("_apps/refresh/app/{appId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Requests every open page of an App to refresh.")]
        [RouteParameterDescription("appId", "The ID of the App")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> RefreshApp(string appId)
        {
            if (await GetClient().Apps.RefreshApp(appId))
            {
                return Ok();
            }

            return NotFound();
        }

        [HttpPost("_apps/refresh/connection/{connectionId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Requests the page of one App connection to refresh.")]
        [RouteParameterDescription("connectionId", "The ID of the App connection")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> RefreshConnection(string connectionId)
        {
            if (await GetClient().Apps.RefreshConnection(connectionId))
            {
                return Ok();
            }

            return NotFound();
        }

        [HttpGet("_apps/metrics")]
        [RouteDescription("Lists the Metrics for every App.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Apps.TrakHoundAppMetrics>))]
        public IActionResult GetMetrics()
        {
            return Ok(_server.AppProvider.GetMetrics() ?? Enumerable.Empty<TrakHound.Apps.TrakHoundAppMetrics>());
        }

        [HttpGet("_apps/metrics/{appId}")]
        [RouteDescription("Gets the Metrics for an App.")]
        [RouteParameterDescription("appId", "The ID of the App")]
        [RouteResponse(200, typeof(TrakHound.Apps.TrakHoundAppMetrics))]
        [RouteResponse(404)]
        public IActionResult GetMetricsById(string appId)
        {
            var metrics = _server.AppProvider.GetMetrics(appId);
            if (metrics != null)
            {
                return Ok(metrics);
            }

            return NotFound();
        }

        [HttpGet("_apps/metrics/connections")]
        [RouteDescription("Lists the Metrics for the open App connections.")]
        [RouteParameterDescription("appId", "The ID of the App to list connections for (every App when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Apps.TrakHoundAppConnectionMetrics>))]
        public IActionResult GetConnectionMetrics([FromQuery] string appId = null)
        {
            return Ok(_server.AppProvider.GetConnectionMetrics(appId) ?? Enumerable.Empty<TrakHound.Apps.TrakHoundAppConnectionMetrics>());
        }


        // Changes are made through a Client so that a Request made with a Module Token is checked against the Module's Zone
        private ITrakHoundClient GetClient() => _server.ClientProvider.GetClient().WithModuleRequestSource(HttpContext);
    }
}
