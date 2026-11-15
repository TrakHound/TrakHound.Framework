// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using TrakHound.Buffers;
using TrakHound.Controllers.Http;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    /// <summary>
    /// System API for reading the Metrics of the Operation Buffers running in this Instance
    /// </summary>
    [ApiController]
    [Route(TrakHoundHttpConstants.BuffersPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Buffers)]
    public class TrakHoundHttpBuffersController : ControllerBase
    {
        /// <summary>
        /// Gets the Metrics for all Buffers, optionally filtered by Driver ID and/or Operation Type
        /// </summary>
        [HttpGet]
        [RouteDescription("Lists the Metrics for every Buffer, optionally filtered by Driver and Operation Type.")]
        [RouteParameterDescription("driverId", "Only include Buffers for this Driver ID")]
        [RouteParameterDescription("operationType", "Only include Buffers for this Operation Type")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundBufferMetrics>))]
        public IActionResult GetMetrics([FromQuery] string driverId = null, [FromQuery] string operationType = null)
        {
            IEnumerable<TrakHoundBufferMetrics> metrics = TrakHoundBufferRegistry.GetMetrics();

            if (!string.IsNullOrEmpty(driverId))
            {
                metrics = metrics.Where(o => string.Equals(o.DriverId, driverId, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(operationType))
            {
                metrics = metrics.Where(o => string.Equals(o.OperationType, operationType, StringComparison.OrdinalIgnoreCase));
            }

            return Ok(metrics);
        }

        /// <summary>
        /// Gets the Metrics for the specified Buffer
        /// </summary>
        [HttpGet("{bufferId}")]
        [RouteDescription("Gets the Metrics for a Buffer.")]
        [RouteParameterDescription("bufferId", "The ID of the Buffer")]
        [RouteResponse(200, typeof(TrakHoundBufferMetrics))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        public IActionResult GetMetrics([FromRoute] string bufferId)
        {
            if (!string.IsNullOrEmpty(bufferId))
            {
                var metrics = TrakHoundBufferRegistry.GetMetrics(bufferId);
                if (metrics != null)
                {
                    return Ok(metrics);
                }

                return NotFound();
            }

            return BadRequest();
        }

        /// <summary>
        /// Gets the Metrics for the specified Buffers (Buffers not found are omitted)
        /// </summary>
        [HttpPost]
        [RouteDescription("Gets the Metrics for several Buffers (Buffers that aren't found are left out).")]
        [RouteParameterDescription("bufferIds", "The IDs of the Buffers")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundBufferMetrics>))]
        [RouteResponse(400)]
        public IActionResult GetMetrics([FromBody] IEnumerable<string> bufferIds)
        {
            if (!bufferIds.IsNullOrEmpty())
            {
                return Ok(TrakHoundBufferRegistry.GetMetrics(bufferIds));
            }

            return BadRequest();
        }
    }
}
