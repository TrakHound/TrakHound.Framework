// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Services.MessageQueues;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.MessageQueueServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpMessageQueuesController : TrakHoundHttpServiceControllerBase
    {
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;


        public TrakHoundHttpMessageQueuesController(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager) : base(instance)
        {
            _webSocketManager = webSocketManager;
        }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the Message Queue Service is available.")]
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
                var serviceResponse = await client.Services.MessageQueues.CheckAvailability(cancellationToken: HttpContext.RequestAborted);
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

        [HttpGet("metrics")]
        [RouteDescription("Gets the Metrics (ex. message counts) of Message Queues.")]
        [RouteParameterDescription("queues", "The Queues to get Metrics for (all Queues when empty)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundMessageQueueMetrics>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(405)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> GetMetrics([FromQuery] IEnumerable<string> queues = null, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                var serviceResponse = await client.Services.MessageQueues.GetMetrics(queues, cancellationToken: HttpContext.RequestAborted);
                if (serviceResponse.Success)
                {
                    return Ok(serviceResponse.Result ?? Enumerable.Empty<TrakHoundMessageQueueMetrics>());
                }
                else
                {
                    return ProcessServiceResponse(serviceResponse);
                }
            }

            return BadRequest();
        }

        [HttpGet("pull")]
        [RouteDescription("Pulls Messages from a Queue (an empty Queue returns an empty list).")]
        [RouteParameterDescription("queue", "The Queue to pull from")]
        [RouteParameterDescription("count", "The maximum number of Messages to pull")]
        [RouteParameterDescription("autoAcknowledge", "Whether to remove the Messages from the Queue when they are pulled (otherwise each must be Acknowledged or Rejected)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundQueueMessage>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Pull([FromQuery] string queue, [FromQuery] int count = 1, [FromQuery] bool autoAcknowledge = false, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null && !string.IsNullOrEmpty(queue))
            {
                var serviceResponse = await client.Services.MessageQueues.Pull(queue, count, autoAcknowledge, cancellationToken: HttpContext.RequestAborted);

                // An empty Queue is an empty list (not 404), so callers can tell it apart from a missing Route or Service
                if (serviceResponse.Success && serviceResponse.Result == null) serviceResponse.Result = Enumerable.Empty<TrakHoundQueueMessage>();

                return ProcessServiceResponse(serviceResponse);
            }

            return BadRequest();
        }

        [Route("subscribe")]
        [RouteDescription("Opens a WebSocket that streams Messages from a Queue (each WebSocket message is one Message as JSON).")]
        [RouteParameterDescription("queue", "The Queue to subscribe to")]
        [RouteParameterDescription("autoAcknowledge", "Whether to remove the Messages from the Queue when they are delivered (otherwise each must be Acknowledged or Rejected)")]
        [RouteParameterDescription("prefetch", "The most unacknowledged Messages delivered at once (0 uses the Service's default)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task Subscribe([FromQuery] string queue, [FromQuery] bool autoAcknowledge = false, [FromQuery] int prefetch = 0, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null)
            {
                Func<Task<ITrakHoundStream<TrakHoundQueueMessage>>> getConsumer = async () =>
                {
                    var serviceResponse = await client.Services.MessageQueues.Subscribe(queue, autoAcknowledge, prefetch, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        return serviceResponse.Result;
                    }
                    else
                    {
                        return null;
                    }
                };

                var formatResponse = (TrakHoundQueueMessage message) =>
                {
                    return message != null ? Json.ConvertToUtf8(message) : null;
                };

                await _webSocketManager.Create<TrakHoundQueueMessage>(HttpContext, getConsumer, null, formatResponse);
            }
        }

        [HttpPost("publish")]
        [RouteDescription("Publishes a Message to a Queue and returns its Message ID.")]
        [RouteParameterDescription("queue", "The Queue to publish to")]
        [RouteParameterDescription("messageId", "The Identifier of the Message (the Service creates one when empty)")]
        [RouteParameterDescription("contentType", "The media type of the Message contents (ex. application/json)")]
        [RouteParameterDescription("header", "A Header to publish with the Message, as name:value (repeat for each Header)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/octet-stream", Description = "The Message contents")]
        [RouteResponse(200, typeof(TrakHoundQueuePublishResult))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Publish([FromQuery] string queue, [FromQuery] string messageId = null, [FromQuery] string contentType = null, [FromQuery] IEnumerable<string> header = null, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null && !string.IsNullOrEmpty(queue) && Request.Body != null)
            {
                var options = new TrakHoundQueuePublishOptions();
                options.MessageId = messageId;
                options.ContentType = contentType;
                options.Headers = TrakHoundQueueContent.ParseHeaders(header);

                var serviceResponse = await client.Services.MessageQueues.Publish(queue, Request.Body, options, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(serviceResponse);
            }

            return BadRequest();
        }

        [HttpPut("acknowledge")]
        [RouteDescription("Acknowledges Messages so they are removed from the Queue, and returns a result for each Delivery ID.")]
        [RouteParameterDescription("queue", "The Queue the Messages were delivered from")]
        [RouteParameterDescription("deliveryIds", "The Delivery IDs of the Messages")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundQueueDeliveryResult>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Acknowledge([FromQuery] string queue, [FromBody] IEnumerable<string> deliveryIds, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null && !string.IsNullOrEmpty(queue) && !deliveryIds.IsNullOrEmpty())
            {
                var serviceResponse = await client.Services.MessageQueues.Acknowledge(queue, deliveryIds, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(serviceResponse);
            }

            return BadRequest();
        }

        [HttpPut("reject")]
        [RouteDescription("Rejects Messages (returning them to the Queue, or discarding them), and returns a result for each Delivery ID.")]
        [RouteParameterDescription("queue", "The Queue the Messages were delivered from")]
        [RouteParameterDescription("deliveryIds", "The Delivery IDs of the Messages")]
        [RouteParameterDescription("requeue", "Whether to return the Messages to the Queue to be delivered again (otherwise they are discarded or dead-lettered)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundQueueDeliveryResult>))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Reject([FromQuery] string queue, [FromBody] IEnumerable<string> deliveryIds, [FromQuery] bool requeue = true, [FromQuery] string routerId = null)
        {
            using var client = GetClient(routerId);
            if (client != null && !string.IsNullOrEmpty(queue) && !deliveryIds.IsNullOrEmpty())
            {
                var serviceResponse = await client.Services.MessageQueues.Reject(queue, deliveryIds, requeue, cancellationToken: HttpContext.RequestAborted);
                return ProcessServiceResponse(serviceResponse);
            }

            return BadRequest();
        }
    }
}
