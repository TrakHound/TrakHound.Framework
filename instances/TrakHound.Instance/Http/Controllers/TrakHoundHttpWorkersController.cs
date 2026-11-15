// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Workers;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.WorkersPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Workers)]
    public class TrakHoundHttpWorkersController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;


        public TrakHoundHttpWorkersController(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager)
        {
            _instance = instance;
            _webSocketManager = webSocketManager;
        }


        [HttpGet("information")]
        public IActionResult GetInformation()
        {
            var informations = _instance.WorkerManager.GetInformation();
            if (informations != null)
            {
                return Ok(informations);
            }

            return NotFound();
        }

        [HttpGet("information/{serviceId}")]
        public IActionResult GetInformation(string serviceId)
        {
            var information = _instance.WorkerManager.GetInformation(serviceId);
            if (information != null)
            {
                return Ok(information);
            }

            return NotFound();
        }

        [HttpPost("information")]
        public IActionResult GetInformation([FromBody] IEnumerable<string> serviceIds)
        {
            var informations = _instance.WorkerManager.GetInformation(serviceIds);
            if (informations != null)
            {
                return Ok(informations);
            }

            return NotFound();
        }


        //[HttpGet]
        //public async Task<IActionResult> Retrieve(
        //    [FromQuery] int skip = 0,
        //    [FromQuery] int take = 100,
        //    [FromQuery] int order = (int)TrakHoundSortOrder.Ascending,
        //    [FromQuery] string routerId = null
        //    )
        //{
        //    var client = _server.ClientProvider.GetClient(routerId);
        //    if (client != null)
        //    {
        //        var services = await client.Workers.Retrieve(skip, take, (TrakHoundSortOrder)order);
        //        if (!services.IsNullOrEmpty())
        //        {
        //            return Ok(services);
        //        }
        //        else
        //        {
        //            return StatusCode(404);
        //        }
        //    }

        //    return BadRequest();
        //}

        [HttpGet("metrics")]
        public IActionResult GetMetrics()
        {
            return Ok(_instance.WorkerManager.GetMetrics() ?? Enumerable.Empty<TrakHoundWorkerMetrics>());
        }

        [HttpGet("metrics/{workerId}")]
        public IActionResult GetMetrics([FromRoute] string workerId)
        {
            var metrics = _instance.WorkerManager.GetMetrics(workerId);
            if (metrics != null)
            {
                return Ok(metrics);
            }

            return NotFound();
        }

        [HttpGet("{workerId}/cpu")]
        public IActionResult GetCpuHistory([FromRoute] string workerId, [FromQuery] long? from = null)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var samples = _instance.WorkerManager.GetCpuHistory(workerId, from?.ToDateTime());
                if (samples != null)
                {
                    return Ok(samples);
                }

                return NotFound();
            }

            return BadRequest();
        }

        [HttpGet("{workerId}/status")]
        public async Task<IActionResult> GetStatus([FromRoute] string workerId, [FromQuery] string routerId = null)
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(workerId))
                {
                    var statuses = await client.Workers.GetStatus(new string[] { workerId });
                    if (!statuses.IsNullOrEmpty())
                    {
                        return Ok(TrakHoundHttpWorkerStatus.Create(statuses));
                    }
                    else
                    {
                        return StatusCode(404);
                    }
                }
            }

            return BadRequest();
        }

        [HttpPost("status")]
        public async Task<IActionResult> GetStatus([FromBody] IEnumerable<string> workerIds, [FromQuery] string routerId = null)
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                if (!workerIds.IsNullOrEmpty())
                {
                    var statuses = await client.Workers.GetStatus(workerIds);
                    if (!statuses.IsNullOrEmpty())
                    {
                        return Ok(TrakHoundHttpWorkerStatus.Create(statuses));
                    }
                    else
                    {
                        return StatusCode(404);
                    }
                }
            }

            return BadRequest();
        }

        [HttpPut("{workerId}/start")]
        [TrakHoundSystemWrite]
        public async Task<IActionResult> StartService([FromRoute] string workerId, [FromQuery] string routerId = null)
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(workerId))
                {
                    var status = await client.Workers.StartWorker(workerId);

                    return Ok(new TrakHoundHttpWorkerStatus(status));
                }
            }

            return BadRequest();
        }

        [HttpPut("{workerId}/stop")]
        [TrakHoundSystemWrite]
        public async Task<IActionResult> StopService([FromRoute] string workerId, [FromQuery] string routerId = null)
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(workerId))
                {
                    var status = await client.Workers.StopWorker(workerId);

                    return Ok(new TrakHoundHttpWorkerStatus(status));
                }
            }

            return BadRequest();
        }

        [Route("status/subscribe")]
        public async Task SubscribeStatus(
            [FromQuery] string routerId = null
            )
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                Func<Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>>> getConsumer = async () =>
                {
                    var clientConsumer = await client.Workers.SubscribeStatus();
                    if (clientConsumer != null)
                    {
                        return clientConsumer;
                    }
                    else
                    {
                        return null;
                    }
                };

                var formatResponse = (TrakHoundWorkerStatusResponse statusResponse) =>
                {
                    if (statusResponse.Timestamp > 0)
                    {
                        var jsonResponse = new TrakHoundWorkerStatusJsonResponse();
                        jsonResponse.WorkerId = statusResponse.WorkerId;
                        jsonResponse.Status = statusResponse.Status;
                        jsonResponse.Timestamp = statusResponse.Timestamp;
                        return jsonResponse.ToJsonBytes(true);
                    }

                    return null;
                };

                await _webSocketManager.Create<TrakHoundWorkerStatusResponse>(HttpContext, getConsumer, null, formatResponse);
            }
        }

        [Route("{workerId}/status/subscribe")]
        public async Task SubscribeStatus(
            [FromRoute] string workerId,
            [FromQuery] string routerId = null
            )
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    Func<Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>>> getConsumer = async () =>
                    {
                        var clientConsumer = await client.Workers.SubscribeStatus(workerId);
                        if (clientConsumer != null)
                        {
                            return clientConsumer;
                        }
                        else
                        {
                            return null;
                        }
                    };

                    var formatResponse = (TrakHoundWorkerStatusResponse statusResponse) =>
                    {
                        if (statusResponse.Timestamp > 0)
                        {
                            var jsonResponse = new TrakHoundWorkerStatusJsonResponse();
                            jsonResponse.WorkerId = statusResponse.WorkerId;
                            jsonResponse.Status = statusResponse.Status;
                            jsonResponse.Timestamp = statusResponse.Timestamp;
                            return jsonResponse.ToJsonBytes(true);
                        }

                        return null;
                    };

                    await _webSocketManager.Create<TrakHoundWorkerStatusResponse>(HttpContext, getConsumer, null, formatResponse);
                }
            }
        }

        [Route("log/subscribe")]
        public async Task SubscribeLog(
           [FromQuery] TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace,
           [FromQuery] string routerId = null
           )
        {
            var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
            if (client != null)
            {
                Func<Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>>> getConsumer = async () =>
                {
                    var clientConsumer = await client.Workers.SubscribeLog(minimumLogLevel);
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

        [Route("{workerId}/log/subscribe")]
        public async Task SubscribeLog(
            [FromRoute] string workerId,
            [FromQuery] TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace,
            [FromQuery] string routerId = null
            )
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    Func<Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>>> getConsumer = async () =>
                    {
                        var clientConsumer = await client.Workers.SubscribeLog(workerId, minimumLogLevel);
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
    }
}
