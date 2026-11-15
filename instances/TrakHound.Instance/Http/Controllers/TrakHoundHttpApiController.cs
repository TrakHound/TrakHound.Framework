// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Instance.Api;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Apis)]
    public class TrakHoundHttpApiController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpApiController(ITrakHoundInstance server)
        {
            _instance = server;
        }


        [HttpGet("_api/information/route")]
        [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiInformation)]
        [RouteDescription("Lists the information for every Api Route.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundApiInformation>))]
        [RouteResponse(404)]
        public IActionResult GetRouteInformations()
        {
            var information = _instance.ApiProvider.GetRouteInformation();
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpGet("_api/information/route/{route}")]
        [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiInformation)]
        [RouteDescription("Gets the information for the Api that serves a Route.")]
        [RouteParameterDescription("route", "The Api Route")]
        [RouteResponse(200, typeof(TrakHoundApiInformation))]
        [RouteResponse(404)]
        public IActionResult GetRouteInformation(string route)
        {
            var information = _instance.ApiProvider.GetRouteInformation()?.FirstOrDefault(o => o.Route == route);
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpGet("_api/information/package")]
        [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiInformation)]
        [RouteDescription("Lists the information for every Api Package.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundApiInformation>))]
        [RouteResponse(404)]
        public IActionResult GetPackageInformations()
        {
            var information = _instance.ApiProvider.GetPackageInformation();
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpGet("_api/information/{apiId}")]
        [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiInformation)]
        [RouteDescription("Gets the information for an Api.")]
        [RouteParameterDescription("apiId", "The ID of the Api")]
        [RouteResponse(200, typeof(TrakHoundApiInformation))]
        [RouteResponse(404)]
        public IActionResult GetInformation(string apiId)
        {
            var information = _instance.ApiProvider.GetInformation(apiId);
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        /// <summary>
        /// The OpenAPI Document describing the API's Endpoints
        /// </summary>
        [HttpGet("_api/openapi/{apiId}")]
        [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiInformation)]
        [RouteDescription("Gets the OpenAPI Document describing an Api's Endpoints.")]
        [RouteParameterDescription("apiId", "The ID of the Api")]
        [RouteResponse(200, typeof(object))]
        [RouteResponse(404)]
        public IActionResult GetOpenApiDocument(string apiId)
        {
            var document = (_instance.ApiProvider as TrakHoundApiProvider)?.GetOpenApiDocument(apiId);
            if (document != null)
            {
                return Content(document, "application/json");
            }

            return NotFound();
        }

        [HttpGet("_api/metrics")]
        [RouteDescription("Lists the Metrics for every Api.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundApiMetrics>))]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.ApiProvider.GetMetrics() ?? Enumerable.Empty<TrakHoundApiMetrics>());
        }

        [HttpGet("_api/requests")]
        [RouteDescription("Lists the most recent Requests to every Api (newest first).")]
        [RouteResponse(200, typeof(TrakHoundApiRecentRequests))]
        public IActionResult GetRecentRequests()
        {
            return Ok(_instance.ApiProvider.GetRecentRequests() ?? new TrakHoundApiRecentRequests());
        }

        [HttpGet("_api/streams")]
        [RouteDescription("Lists the open and most recently closed Streams of every Api.")]
        [RouteResponse(200, typeof(TrakHoundApiStreams))]
        public IActionResult GetStreams()
        {
            return Ok(_instance.ApiProvider.GetStreams() ?? new TrakHoundApiStreams());
        }

        [HttpPost("_api/streams/{streamId}/close")]
        [TrakHoundSystemWrite]
        [RouteDescription("Closes an open Api Stream (its client sees the Stream end).")]
        [RouteParameterDescription("streamId", "The ID of the Stream")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public IActionResult CloseStream(string streamId)
        {
            if (_instance.ApiProvider.CloseStream(streamId))
            {
                return Ok();
            }

            return NotFound();
        }

        [HttpGet("_api/metrics/{apiId}")]
        [RouteDescription("Gets the Metrics for an Api.")]
        [RouteParameterDescription("apiId", "The ID of the Api")]
        [RouteResponse(200, typeof(TrakHoundApiMetrics))]
        [RouteResponse(404)]
        public IActionResult GetMetricsById(string apiId)
        {
            var metrics = _instance.ApiProvider.GetMetrics(apiId);
            if (metrics != null)
            {
                return Ok(metrics);
            }

            return NotFound();
        }

        [HttpPost("_api/reload/{apiId}")]
        [TrakHoundSystemWrite]
        [RouteDescription("Reloads the Configuration of an Api.")]
        [RouteParameterDescription("apiId", "The ID of the Api")]
        [RouteResponse(200)]
        [RouteResponse(404)]
        public async Task<IActionResult> ReloadConfiguration(string apiId)
        {
            // Through a Client so that a Request made with a Module Token is checked against the Module's Zone
            var client = _instance.ClientProvider.GetClient().WithModuleRequestSource(HttpContext);
            if (await client.Api.ReloadConfiguration(apiId))
            {
                return Ok();
            }

            return NotFound();
        }
    }
}
