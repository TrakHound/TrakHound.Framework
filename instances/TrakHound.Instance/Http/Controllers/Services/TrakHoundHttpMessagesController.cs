// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Services.Messages;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.MessageServicePrefix)]
    [TrakHoundServiceOperations]
    public class TrakHoundHttpMessagesController : TrakHoundHttpServiceControllerBase
    {
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;


        public TrakHoundHttpMessagesController(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager) : base(instance)
        {
            _webSocketManager = webSocketManager;
        }


        [HttpGet("availability")]
        [RouteDescription("Checks whether the Message Service is available.")]
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
                var serviceResponse = await client.Services.Messages.CheckAvailability(cancellationToken: HttpContext.RequestAborted);
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


        /// <summary>
        /// Opens a WebSocket that streams Messages for one or more Topics (repeat the "topic" query parameter for multiple Topics)
        /// </summary>
        [Route("subscribe")]
        [RouteDescription("Opens a WebSocket that streams Messages for one or more Topics.")]
        [RouteParameterDescription("topics", "The Topics to subscribe to (repeat the parameter for multiple Topics)")]
        [RouteParameterDescription("clientId", "The Client ID to subscribe as")]
        [RouteParameterDescription("qos", "The Quality of Service (0, 1, or 2)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task Subscribe(
            [FromQuery(Name = "topic")] string[] topics,
            [FromQuery] string clientId = null,
            [FromQuery] int qos = 0,
            [FromQuery] string routerId = null
            )
        {
            // Validate before the WebSocket is accepted so the caller receives a 400 status
            topics = topics?.Where(o => !string.IsNullOrEmpty(o)).ToArray();
            if (topics.IsNullOrEmpty() || qos < 0 || qos > 2)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var client = GetClient(routerId);
            if (client != null)
            {
                Func<Task<ITrakHoundStream<TrakHoundMessageResponse>>> getStream = async () =>
                {
                    var serviceResponse = await client.Services.Messages.Subscribe(topics, clientId, qos, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        return serviceResponse.Result;
                    }
                    else
                    {
                        return null;
                    }
                };

                var formatResponse = (TrakHoundMessageResponse messageResponse) =>
                {
                    return TrakHoundHttpMessageResponse.Create(messageResponse);
                };

                // Input from the WebSocket isn't used
                await _webSocketManager.Create<TrakHoundMessageResponse>(HttpContext, getStream, null, formatResponse);
            }
        }

        [HttpPost("publish")]
        [RouteDescription("Publishes a Message to a Topic.")]
        [RouteParameterDescription("topic", "The Topic to publish to")]
        [RouteParameterDescription("retain", "Whether the Message is Retained for new subscribers")]
        [RouteParameterDescription("qos", "The Quality of Service (0, 1, or 2)")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        [RouteRequestBody("application/octet-stream", Description = "The Message contents (empty to clear a Retained Message)", Required = false)]
        [RouteResponse(200, typeof(string))]
        [RouteResponse(400)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        [RouteResponse(503)]
        public async Task<IActionResult> Publish(
            [FromQuery] string topic,
            [FromQuery] bool retain = false,
            [FromQuery] int qos = 0,
            [FromQuery] string routerId = null
            )
        {
            if (qos < 0 || qos > 2) return BadRequest($"Invalid QoS ({qos}) : Must be 0, 1, or 2");

            using var client = GetClient(routerId);
            if (client != null)
            {
                // An empty Body is valid (ex. to clear a Retained Message)
                if (!string.IsNullOrEmpty(topic) && Request.Body != null)
                {
                    var serviceResponse = await client.Services.Messages.Publish(topic, Request.Body, retain, qos, cancellationToken: HttpContext.RequestAborted);
                    if (serviceResponse.Success)
                    {
                        return Ok($"Topic = {topic} Published Successfully");
                    }
                    else
                    {
                        return ProcessServiceResponse(serviceResponse);
                    }
                }
            }

            return BadRequest();
        }

        /// <summary>
        /// Opens a WebSocket that publishes each frame it receives as a Message. Each frame uses the Message frame format; empty Topic, Retain and QoS fields use the query parameters.
        /// Only failures are sent back, as JSON (topic, statusCode, message). Each Message is authorized as a Publish, the same as POST publish
        /// </summary>
        [Route("publish/stream")]
        [RouteDescription("Opens a WebSocket that publishes each Message frame it receives and sends back the Messages that failed.")]
        [RouteParameterDescription("topic", "The Topic for frames that leave the Topic empty")]
        [RouteParameterDescription("retain", "Whether frames that leave Retain empty are Retained")]
        [RouteParameterDescription("qos", "The Quality of Service (0, 1, or 2) for frames that leave QoS empty")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task PublishStream(
            [FromQuery] string topic = null,
            [FromQuery] bool retain = false,
            [FromQuery] int qos = 0,
            [FromQuery] string routerId = null
            )
        {
            // Validate before the WebSocket is accepted so the caller receives a 400 status
            if (TrakHoundMessageTopics.GetPublishError(topic, qos, requireTopic: false) != null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var client = GetClient(routerId);
            if (client == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // The Router is resolved once for the WebSocket instead of for every frame
            var publisher = ((TrakHound.Clients.TrakHoundInstanceMessageServiceClient)client.Services.Messages).CreatePublisher();

            // The Stream only carries failures back to the client (frames are published as they arrive)
            Func<Task<ITrakHoundStream<TrakHoundMessagePublishFailure>>> getStream = () =>
            {
                return Task.FromResult<ITrakHoundStream<TrakHoundMessagePublishFailure>>(new TrakHoundStream<TrakHoundMessagePublishFailure>());
            };

            Func<ITrakHoundStream<TrakHoundMessagePublishFailure>, byte[], Task> onInput = async (stream, frame) =>
            {
                TrakHoundMessagePublishFailure failure;

                if (TrakHoundHttpMessageResponse.TryParsePublish(frame, topic, retain, qos, out var message, out var error))
                {
                    var serviceResponse = await publisher.Publish(message.Topic, message.Content, message.Retain, message.Qos, HttpContext.RequestAborted);
                    failure = TrakHoundMessagePublishFailure.FromResponse(message.Topic, serviceResponse);
                }
                else
                {
                    failure = new TrakHoundMessagePublishFailure(message.Topic, StatusCodes.Status400BadRequest, error);
                }

                if (failure != null) await stream.Push(failure);
            };

            var formatResponse = (TrakHoundMessagePublishFailure failure) => Json.Convert(failure, indented: false).ToUtf8Bytes();

            await _webSocketManager.CreateWithInput(HttpContext, getStream, onInput, formatResponse);
        }
    }
}
