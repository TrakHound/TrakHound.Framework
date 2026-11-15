// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// Routes Message Service Requests to the Drivers and Routers configured on the Service Router
    /// </summary>
    public class TrakHoundMessageRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a new Message Router for the specified Service Router
        /// </summary>
        public TrakHoundMessageRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }

        /// <summary>
        /// Checks whether a Message Service is available through the Router. The optional requestId identifies the Request across Routers
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageService, TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.CheckAvailability(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Messages.CheckAvailability(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("CheckAvailability", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageRoutes.Messages),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Subscribes to Messages published on any of the Topics through the Router using the Client ID and QoS level
        /// </summary>
        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string clientId, IEnumerable<string> topics, int qos, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageService, ITrakHoundStream<TrakHoundMessageResponse>>, Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Subscribe(clientId, topics, qos, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<ITrakHoundStream<TrakHoundMessageResponse>>, Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Messages.Subscribe(clientId, topics, qos, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Subscribe", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageRoutes.Messages),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Publishes the content as a Message on the Topic through the Router. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, string requestId = null, CancellationToken cancellationToken = default)
        {
            // A fallback Target is only given the content from its start (see RouteContent)
            var routeContent = new RouteContent(content);

            // Set Query Driver Command
            Func<RouteTargetServiceRequest<ITrakHoundMessageService>, Task<TrakHoundServiceResponse>> serviceCommand = async (serviceRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable();
                return await serviceRequest.Service.Publish(topic, routeContent.Stream, retain, qos, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = async (routerRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable();
                return await routerRequest.Router.Messages.Publish(topic, routeContent.Stream, retain, qos, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Publish", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageRoutes.Messages),
                serviceCommand,
                routerCommand
                );

            return response;
        }
    }
}
