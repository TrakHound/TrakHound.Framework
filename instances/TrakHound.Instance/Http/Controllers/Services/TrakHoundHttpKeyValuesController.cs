// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.KeyValueServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpKeyValuesController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpKeyValuesController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the KeyValue Service is available.")]
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
                var serviceResponse = await client.Services.KeyValues.CheckAvailability(cancellationToken: HttpContext.RequestAborted);
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

            return BadRequest();
        }

        [HttpGet("list/collections")]
        [RouteDescription("Lists the KeyValue Collections.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListCollections([FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var serviceResponse = await client.Services.KeyValues.ListCollections(cancellationToken: HttpContext.RequestAborted);
                if (serviceResponse.Success)
                {
                    if (!serviceResponse.Result.IsNullOrEmpty())
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

            return BadRequest();
        }

        [HttpGet("list/keys")]
        [RouteDescription("Lists the Keys in a Collection that match a pattern.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("pattern", "The pattern the Keys must match (all Keys when empty)")]
        [RouteParameterDescription("skip", "The number of Keys to skip")]
        [RouteParameterDescription("take", "The maximum number of Keys to return")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListKeys([FromQuery] string collection, [FromQuery] string pattern, [FromQuery] int skip = 0, [FromQuery] int take = 1000, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    var serviceResponse = await client.Services.KeyValues.ListKeys(collection, pattern, skip, take, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        if (!serviceResponse.Result.IsNullOrEmpty())
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
            }

            return BadRequest();
        }

        [HttpGet]
        [RouteDescription("Reads the value of a Key.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("key", "The Key to read")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(string))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Read(
            [FromQuery] string collection,
            [FromQuery] string key = null,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        var serviceResponse = await client.Services.KeyValues.Read(collection, key, cancellationToken: HttpContext.RequestAborted);
                        if (serviceResponse.Success)
                        {
                            var keyValueResponse = serviceResponse.Result;
                            if (keyValueResponse != null)
                            {
                                return Ok(keyValueResponse);
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
                        return BadRequest("Key Not Specified");
                    }
                }
            }

            return BadRequest();
        }

        [HttpPost]
        [RouteDescription("Reads the values of multiple Keys.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("keys", "The Keys to read")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(Dictionary<string, string>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ReadMulitple(
            [FromQuery] string collection, 
            [FromBody] IEnumerable<string> keys = null,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    if (!keys.IsNullOrEmpty())
                    {
                        var serviceResponse = await client.Services.KeyValues.Read(collection, keys, cancellationToken: HttpContext.RequestAborted);
                        if (serviceResponse.Success)
                        {
                            var keyValueResponse = serviceResponse.Result;
                            if (keyValueResponse != null && !keyValueResponse.Values.IsNullOrEmpty())
                            {
                                return Ok(keyValueResponse.Values);
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
                        return BadRequest("Key(s) Not Specified");
                    }
                }
            }

            return BadRequest();
        }

        [HttpGet("count")]
        [RouteDescription("Counts the Keys in a Collection.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(int))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Count(
            [FromQuery] string collection,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    var serviceResponse = await client.Services.KeyValues.Count(collection, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        return Ok(serviceResponse.Result);
                    }
                    else
                    {
                        return ProcessServiceResponse(serviceResponse);
                    }
                }
            }

            return BadRequest();
        }

        [HttpPut("publish")]
        [HttpPost("publish")]
        [RouteDescription("Publishes one or more Key Values to a Collection.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("key", "The Key to publish (when no entries are sent in the body)")]
        [RouteParameterDescription("value", "The value to publish for the key")]
        [RouteParameterDescription("entries", "The Values to publish, keyed by Key")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Publish(
            [FromQuery] string collection,
            [FromQuery] string key = null,
            [FromQuery] string value = null,
            [FromBody] Dictionary<string, string> entries = null,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    if (entries.IsNullOrEmpty() && !string.IsNullOrEmpty(key))
                    {
                        entries = new Dictionary<string, string>();
                        entries.Add(key, value);
                    }

                    if (!entries.IsNullOrEmpty())
                    {
                        var serviceResponse = await client.Services.KeyValues.Publish(collection, entries, cancellationToken: HttpContext.RequestAborted);
                        if (serviceResponse.Success)
                        {
                            return Ok();
                        }
                        else
                        {
                            return ProcessServiceResponse(serviceResponse);
                        }
                    }
                    else
                    { 
                        return BadRequest("Entries Not Specified");
                    }
                }
                else
                {
                    return BadRequest("Collection Not Specified");
                }
            }

            return BadRequest();
        }

        [HttpDelete]
        [RouteDescription("Deletes one or more Keys from a Collection.")]
        [RouteParameterDescription("collection", "The KeyValue Collection")]
        [RouteParameterDescription("key", "The Key to delete (when no keys are sent in the body)")]
        [RouteParameterDescription("keys", "The Keys to delete")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Delete(
            [FromQuery] string collection, 
            [FromQuery] string key = null,
            [FromBody] IEnumerable<string> keys = null,
            [FromQuery] string routerId = null
            )
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    if (keys.IsNullOrEmpty() && !string.IsNullOrEmpty(key)) keys = new string[] { key };

                    if (!keys.IsNullOrEmpty())
                    {
                        var serviceResponse = await client.Services.KeyValues.Delete(collection, keys, cancellationToken: HttpContext.RequestAborted);
                        if (serviceResponse.Success)
                        {
                            return Ok();
                        }
                        else
                        {
                            return ProcessServiceResponse(serviceResponse);
                        }
                    }
                    else
                    {
                        return BadRequest("Key(s) Not Specified");
                    }
                }
            }

            return BadRequest();
        }
    }
}
