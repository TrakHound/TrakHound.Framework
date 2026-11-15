// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Services.Blobs;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.BlobServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpBlobsController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpBlobsController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the Blob Service is available.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceAvailabilityResponse))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> CheckAvailability([FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                var serviceResponse = await client.Services.Blobs.CheckAvailability(cancellationToken: HttpContext.RequestAborted);
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
        [RouteDescription("Lists the Blob Collections.")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundBlobCollectionJson>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListCollections([FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                var serviceResponse = await client.Services.Blobs.ListCollections(cancellationToken: HttpContext.RequestAborted);
                if (serviceResponse.Success)
                {
                    if (!serviceResponse.Result.IsNullOrEmpty())
                    {
                        return Ok(serviceResponse.Result.Select(o => new TrakHoundBlobCollectionJson(o)));
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
        [RouteDescription("Lists the Keys of the Blobs in a Collection.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("skip", "The number of Keys to skip")]
        [RouteParameterDescription("take", "The maximum number of Keys to return")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> ListKeys([FromQuery] string collection, [FromQuery] int skip = 0, [FromQuery] int take = 1000, [FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    var serviceResponse = await client.Services.Blobs.ListKeys(collection, skip, take, cancellationToken: HttpContext.RequestAborted);
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

        [HttpGet("list")]
        [RouteDescription("Lists the Blobs in a Collection.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("skip", "The number of Blobs to skip")]
        [RouteParameterDescription("take", "The maximum number of Blobs to return")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundBlobJson>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> List([FromQuery] string collection, [FromQuery] int skip = 0, [FromQuery] int take = 1000, [FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection))
                {
                    var serviceResponse = await client.Services.Blobs.List(collection, skip, take, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        if (!serviceResponse.Result.IsNullOrEmpty())
                        {
                            return Ok(serviceResponse.Result.Select(o => new TrakHoundBlobJson(o)));
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
        [RouteDescription("Reads the information (Content Type, Size, Metadata) of a Blob.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("key", "The Key of the Blob")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(TrakHoundBlobJson))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Read([FromQuery] string collection, [FromQuery] string key, [FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
                {
                    var serviceResponse = await client.Services.Blobs.Read(collection, key, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        var blobResponse = serviceResponse.Result;
                        if (blobResponse.IsValid)
                        {
                            return Ok(new TrakHoundBlobJson(blobResponse));
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

        [HttpGet("download")]
        [RouteDescription("Downloads the contents of a Blob.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("key", "The Key of the Blob")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(Stream))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Download([FromQuery] string collection, [FromQuery] string key, [FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
                {
                    var serviceReadResponse = await client.Services.Blobs.Read(collection, key, cancellationToken: HttpContext.RequestAborted);
                    if (serviceReadResponse.Success)
                    {
                        var blobResponse = serviceReadResponse.Result;
                        if (blobResponse.IsValid)
                        {
                            // Download from the Service that answered the Read (one Service call instead of routing again, and the content matches the Content Type)
                            var blobsClient = (TrakHound.Clients.Services.TrakHoundInstanceBlobServiceClient)client.Services.Blobs;
                            var serviceDownloadResponse = await blobsClient.DownloadFromService(serviceReadResponse.ServiceId, collection, key, cancellationToken: HttpContext.RequestAborted);
                            if (serviceDownloadResponse.Success)
                            {
                                if (serviceDownloadResponse.Result != null)
                                {
                                    return File(serviceDownloadResponse.Result, blobResponse.ContentType);
                                }
                                else
                                {
                                    return NotFound();
                                }
                            }
                            else
                            {
                                return ProcessServiceResponse(serviceDownloadResponse);
                            }
                        }
                        else
                        {
                            return NotFound();
                        }
                    }
                    else
                    {
                        return ProcessServiceResponse(serviceReadResponse);
                    }
                }
            }

            return BadRequest();
        }

        [HttpPost("upload")]
        [RouteDescription("Uploads the contents of a Blob.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("key", "The Key of the Blob")]
        [RouteParameterDescription("contentType", "The Content Type of the Blob (ex. image/png)")]
        [RouteParameterDescription("overwrite", "Whether to replace an existing Blob with the same Key")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/octet-stream", Description = "The Blob contents")]
        [RouteResponse(200, typeof(string))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Upload(
            [FromQuery] string collection,
            [FromQuery] string key,
            [FromQuery] string contentType, 
            [FromQuery] bool overwrite = true,
            [FromQuery] string routerId = null
            )
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(contentType) && Request.Body != null)
                {
                    //if (Request.Body.Length > 0)
                    //{
                        var serviceResponse = await client.Services.Blobs.Upload(collection, key, Request.Body, contentType, overwrite: overwrite, cancellationToken: HttpContext.RequestAborted);
                        if (serviceResponse.Success)
                        {
                            return Ok($"Key = {key} Published Successfully");
                        }
                        else
                        {
                            return ProcessServiceResponse(serviceResponse);
                        }
                    //}
                    //else
                    //{
                    //    return BadRequest("No Request Body Found : Length = 0");
                    //}
                }
            }

            return BadRequest();
        }

        [HttpDelete]
        [RouteDescription("Deletes a Blob.")]
        [RouteParameterDescription("collection", "The Blob Collection")]
        [RouteParameterDescription("key", "The Key of the Blob")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(string))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Delete([FromQuery] string collection, [FromQuery] string key, [FromQuery] string routerId = null)
        {
            var client = GetClient(routerId);
            if (client != null)
            {
                if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
                {
                    var serviceResponse = await client.Services.Blobs.Delete(collection, key, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        return Ok($"Key = {key} Deleted Successfully");
                    }
                    else
                    {
                        return ProcessServiceResponse(serviceResponse);
                    }
                }
            }

            return BadRequest();
        }
    }
}
