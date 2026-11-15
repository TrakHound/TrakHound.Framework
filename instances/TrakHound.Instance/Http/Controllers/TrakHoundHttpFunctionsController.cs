// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Functions;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.FunctionsPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Functions)]
    public class TrakHoundHttpFunctionsController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;


        public TrakHoundHttpFunctionsController(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager)
        {
            _instance = instance;
            _webSocketManager = webSocketManager;
        }


        [HttpGet("information")]
        [RouteDescription("Lists the information for every Function.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundFunctionInformation>))]
        [RouteResponse(404)]
        public IActionResult GetInformation()
        {
            var informations = _instance.FunctionManager.GetInformation();
            if (informations != null)
            {
                return Ok(informations);
            }

            return NotFound();
        }

        [HttpGet("information/{functionId}")]
        [RouteDescription("Gets the information for a Function.")]
        [RouteParameterDescription("functionId", "The ID of the Function")]
        [RouteResponse(200, typeof(TrakHoundFunctionInformation))]
        [RouteResponse(404)]
        public IActionResult GetInformation(string functionId)
        {
            var information = _instance.FunctionManager.GetInformation(functionId);
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpPost("information")]
        [RouteDescription("Gets the information for several Functions.")]
        [RouteParameterDescription("functionIds", "The IDs of the Functions")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundFunctionInformation>))]
        [RouteResponse(404)]
        public IActionResult GetInformation([FromBody] IEnumerable<string> functionIds)
        {
            var informations = _instance.FunctionManager.GetInformation(functionIds);
            if (informations != null)
            {
                return Ok(informations);
            }

            return NotFound();
        }


        [HttpGet("metrics")]
        [RouteDescription("Lists the Metrics for every Function.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundFunctionMetrics>))]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.FunctionManager.GetMetrics() ?? Enumerable.Empty<TrakHoundFunctionMetrics>());
        }

        [HttpGet("metrics/{functionId}")]
        [RouteDescription("Gets the Metrics for a Function.")]
        [RouteParameterDescription("functionId", "The ID of the Function")]
        [RouteResponse(200, typeof(TrakHoundFunctionMetrics))]
        [RouteResponse(404)]
        public IActionResult GetMetrics(string functionId)
        {
            var metrics = _instance.FunctionManager.GetMetrics(functionId);
            if (metrics != null)
            {
                return Ok(metrics);
            }

            return NotFound();
        }


        // POST only (a GET would let another site run a Function with a signed in browser's Session Cookie)
        [HttpPost("run")]
        [TrakHoundSystemWrite]
        [RouteDescription("Runs a Function with Input Parameters and returns the Function's Output Parameters.")]
        [RouteParameterDescription("functionId", "The ID of the Function to run")]
        [RouteParameterDescription("inputParameters", "The Input Parameters (Name => Value). Optional")]
        [RouteParameterDescription("runId", "The ID to give the Run (a new ID when empty)")]
        [RouteParameterDescription("timestamp", "The Unix timestamp (in nanoseconds) of the Run (now when 0)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IReadOnlyDictionary<string, string>))]
        [RouteResponse(400)]
        public async Task<IActionResult> Run([FromQuery] string functionId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] Dictionary<string, string> inputParameters = null, [FromQuery] string runId = null, [FromQuery] long timestamp = 0, [FromQuery] string routerId = null)
        {
            if (!string.IsNullOrEmpty(functionId))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    var response = await client.Functions.Run(functionId, inputParameters, runId, timestamp);
                    return ProcessResponse(response);
                }
            }

            return BadRequest();
        }

        [Route("run/{runId}/status/subscribe")]
        [RouteDescription("Streams the Status of a Function Run over a WebSocket.")]
        [RouteParameterDescription("runId", "The ID of the Run")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task SubscribeStatus(
            [FromRoute] string runId,
            [FromQuery] string routerId = null
            )
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    Func<Task<ITrakHoundStream<TrakHoundFunctionStatusResponse>>> getConsumer = async () =>
                    {
                        var clientConsumer = await client.Functions.SubscribeRunStatus(runId);
                        if (clientConsumer != null)
                        {
                            return clientConsumer;
                        }
                        else
                        {
                            return null;
                        }
                    };

                    var formatResponse = (TrakHoundFunctionStatusResponse statusResponse) =>
                    {
                        if (statusResponse.Timestamp > 0)
                        {
                            var jsonResponse = new TrakHoundFunctionStatusJsonResponse();
                            jsonResponse.Status = statusResponse.Status;
                            jsonResponse.Timestamp = statusResponse.Timestamp;
                            return jsonResponse.ToJsonBytes(true);
                        }

                        return null;
                    };

                    await _webSocketManager.Create<TrakHoundFunctionStatusResponse>(HttpContext, getConsumer, null, formatResponse);
                }
            }
        }

        [Route("run/{runId}/log/subscribe")]
        [RouteDescription("Streams the Log of a Function Run over a WebSocket.")]
        [RouteParameterDescription("runId", "The ID of the Run")]
        [RouteParameterDescription("minimumLogLevel", "The lowest Log Level to include")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task SubscribeLog(
            [FromRoute] string runId,
            [FromQuery] TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace,
            [FromQuery] string routerId = null
            )
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    Func<Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>>> getConsumer = async () =>
                    {
                        var clientConsumer = await client.Functions.SubscribeRunLog(runId, minimumLogLevel);
                        if (clientConsumer != null)
                        {
                            return clientConsumer;
                        }
                        else
                        {
                            return null;
                        }
                    };

                    var formatResponse = (IEnumerable<TrakHoundLogItem> items) =>
                    {
                        if (!items.IsNullOrEmpty())
                        {
                            var jsonItems = new List<TrakHoundLogItemJson>();
                            foreach (var item in items)
                            {
                                jsonItems.Add(new TrakHoundLogItemJson(item));
                            }
                            return jsonItems.ToJsonBytes(true);
                        }

                        return null;
                    };

                    await _webSocketManager.Create<IEnumerable<TrakHoundLogItem>>(HttpContext, getConsumer, null, formatResponse);
                }
            }
        }


        private IActionResult ProcessResponse(TrakHoundFunctionResponse response)
        {
            // No Status Code means the Function wasn't Run (ex. it isn't loaded). Status Code 0 would be sent as a malformed response
            if (response.StatusCode <= 0) return NotFound();

            return StatusCode(response.StatusCode, response.Parameters);
        }
    }
}
