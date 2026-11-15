// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Services.Tables;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.TableServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpTablesController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpTablesController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the Table Service is available.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceAvailabilityResponse))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> CheckAvailability([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var serviceResponse = await client.Services.Tables.CheckAvailability(cancellationToken: HttpContext.RequestAborted);
                if (serviceResponse.Success)
                {
                    if (serviceResponse.Result != null)
                    {
                        return Ok(serviceResponse.Result);
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                else
                {
                    return ProcessServiceResponse(serviceResponse);
                }
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpGet("list")]
        [RouteDescription("Lists the Tables.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHoundTable[]))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> List(
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var response = await client.Services.Tables.List(cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpGet("columns")]
        [RouteDescription("Gets the Columns of a Table.")]
        [RouteParameterDescription("tableName", "The name of the Table")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHoundTableColumn[]))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> GetColumns(
            [FromQuery] string tableName,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var response = await client.Services.Tables.GetColumns(tableName, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpGet("dialects")]
        [RouteDescription("Gets the SQL Dialects the Table Service runs, in order of preference.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(string[]))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> GetDialects(
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var response = await client.Services.Tables.GetDialects(cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpPost("execute")]
        [RouteDescription("Runs the steps of a Table Request and returns a Result for each step. The Table Service generates or selects the SQL for its own database.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/json", typeof(TrakHoundTableRequest))]
        [RouteResponse(200, typeof(TrakHoundTableStepResult[]))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(405)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Execute(
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var request = await GetRequestBodyJson<TrakHoundTableRequest>(Request.Body);
                var response = await client.Services.Tables.Execute(request, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpPost]
        [RouteDescription("Executes a Query that does not return results (ex. INSERT or UPDATE).")]
        [RouteParameterDescription("timeout", "The Query timeout in seconds (the Service default when empty)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("text/plain", typeof(string), Description = "The Query as text, or a JSON Query Request with the Query and its Parameters")]
        [RouteRequestBody("application/json", typeof(TrakHoundTableQueryRequest))]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ExecuteNonQuery(
            [FromQuery] int? timeout = null,
            [FromQuery] string routerId = null
           )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var (query, parameters) = await GetQueryRequest();
                var response = await client.Services.Tables.ExecuteNonQuery(query, timeout, parameters, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }

        [HttpPost("query")]
        [RouteDescription("Executes a Query and returns its results.")]
        [RouteParameterDescription("timeout", "The Query timeout in seconds (the Service default when empty)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("text/plain", typeof(string), Description = "The Query as text, or a JSON Query Request with the Query and its Parameters")]
        [RouteRequestBody("application/json", typeof(TrakHoundTableQueryRequest))]
        [RouteResponse(200, typeof(TrakHoundTableResult[]))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ExecuteQuery(
            [FromQuery] int? timeout = null,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var (query, parameters) = await GetQueryRequest();
                var response = await client.Services.Tables.ExecuteQuery(query, timeout, parameters, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(response);
            }
            else
            {
                return StatusCode(500, "Error Getting TrakHound Client");
            }
        }


        // A JSON body contains the Query and Parameters. Any other body is the raw Query
        private async Task<(string Query, TrakHoundTableParameterCollection Parameters)> GetQueryRequest()
        {
            if (Request.ContentType != null && Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var queryRequest = await GetRequestBodyJson<TrakHoundTableQueryRequest>(Request.Body);
                return (queryRequest?.Query, queryRequest?.GetParameters());
            }

            return (await GetRequestBodyString(Request.Body), null);
        }
    }
}
