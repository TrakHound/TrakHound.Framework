// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.TimeSeriesServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpTimeSeriesController : TrakHoundHttpServiceControllerBase
    {
        private const string _clientError = "Error Getting TrakHound Client";


        public TrakHoundHttpTimeSeriesController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the Time Series Service is available.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceAvailabilityResponse))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> CheckAvailability([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            return ProcessServiceResponse(await client.Services.TimeSeries.CheckAvailability(cancellationToken: HttpContext.RequestAborted));
        }

        [HttpGet("list/measurements")]
        [RouteDescription("Lists the Measurements.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListMeasurements([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            return ProcessServiceResponse(await client.Services.TimeSeries.ListMeasurements(cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost("list/series")]
        [RouteDescription("Lists the Series of a Measurement that have all of the Tags in the request body.")]
        [RouteParameterDescription("measurement", "The Measurement")]
        [RouteParameterDescription("skip", "The number of Series to skip")]
        [RouteParameterDescription("take", "The maximum number of Series to return")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(Dictionary<string, string>))]
        [RouteResponse(200, typeof(TrakHoundTimeSeries[]))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListSeries(
            [FromQuery] string measurement,
            [FromQuery] int skip = 0,
            [FromQuery] int take = TrakHoundTimeSeriesQuery.DefaultTake,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var tags = await GetRequestBodyJson<Dictionary<string, string>>(Request.Body);
            return ProcessServiceResponse(await client.Services.TimeSeries.ListSeries(measurement, tags, skip, take, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpGet("list/fields")]
        [RouteDescription("Lists the Fields of a Measurement and their types.")]
        [RouteParameterDescription("measurement", "The Measurement")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHoundTimeSeriesField[]))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListFields(
            [FromQuery] string measurement,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            return ProcessServiceResponse(await client.Services.TimeSeries.ListFields(measurement, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost("write")]
        [RouteDescription("Writes Points. A Point with the same Measurement, Tags and Timestamp replaces only the Fields it supplies and their Qualities. A Bad Field can have a null Value. A number without a decimal point is a whole number, which creates a new Field as a long but can be written to an existing double Field.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(TrakHoundTimeSeriesPoint[]))]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(405)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Write([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var points = await GetRequestBodyJson<TrakHoundTimeSeriesPoint[]>(Request.Body);
            return ProcessServiceResponse(await client.Services.TimeSeries.Write(points, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost("read")]
        [RouteDescription("Reads the Points between start (inclusive) and stop (exclusive), with a Result for each Series that has hasMore set if take left out Points. Each tag can be one value or an array of values.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(TrakHoundTimeSeriesQuery))]
        [RouteResponse(200, typeof(TrakHoundTimeSeriesResult[]))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Read([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var query = await GetRequestBodyJson<TrakHoundTimeSeriesQuery>(Request.Body);
            return ProcessServiceResponse(await client.Services.TimeSeries.Read(query, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost("read/latest")]
        [RouteDescription("Reads the newest Value of each Field of each Series. Fields can have different timestamps. Only the measurement, tags and fields of the request body are used.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(TrakHoundTimeSeriesQuery))]
        [RouteResponse(200, typeof(TrakHoundTimeSeriesLatestResult[]))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ReadLatest([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var query = await GetRequestBodyJson<TrakHoundTimeSeriesQuery>(Request.Body);
            if (query == null) return BadRequest("A Query is required");

            return ProcessServiceResponse(await client.Services.TimeSeries.ReadLatest(query.Measurement, query.Tags, query.Fields, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost("aggregate")]
        [RouteDescription("Calculates the functions for each interval between start (inclusive) and stop (exclusive), with a Result for each Series.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(TrakHoundTimeSeriesAggregateQuery))]
        [RouteResponse(200, typeof(TrakHoundTimeSeriesResult[]))]
        [RouteResponse(400)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Aggregate([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var query = await GetRequestBodyJson<TrakHoundTimeSeriesAggregateQuery>(Request.Body);
            return ProcessServiceResponse(await client.Services.TimeSeries.Aggregate(query, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpDelete]
        [RouteDescription("Deletes the Points between start (inclusive) and stop (exclusive) of each Series that has all of the Tags in the request body. Returns 405 if no Service can delete Points.")]
        [RouteParameterDescription("measurement", "The Measurement")]
        [RouteParameterDescription("start", "The start of the range (inclusive)")]
        [RouteParameterDescription("stop", "The end of the range (exclusive)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(Dictionary<string, string>))]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(405)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Delete(
            [FromQuery] string measurement,
            [FromQuery] DateTimeOffset start,
            [FromQuery] DateTimeOffset stop,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client == null) return StatusCode(500, _clientError);

            var tags = await GetRequestBodyJson<Dictionary<string, string>>(Request.Body);
            return ProcessServiceResponse(await client.Services.TimeSeries.Delete(measurement, tags, start, stop, cancellationToken: HttpContext.RequestAborted));
        }
    }
}
