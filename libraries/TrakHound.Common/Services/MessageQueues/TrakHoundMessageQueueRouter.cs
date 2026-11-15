// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// Routes Message Queue Requests to the Service and Router Targets configured for the "MessageQueues" Route of a Service Router
    /// </summary>
    public class TrakHoundMessageQueueRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a Message Queue Router for the specified Service Router
        /// </summary>
        public TrakHoundMessageQueueRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }

        /// <summary>
        /// Checks the availability of the Message Queue Services matched by the Router
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.CheckAvailability(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.MessageQueues.CheckAvailability(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("CheckAvailability", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Pulls up to the specified number of Messages from the Queue
        /// (when autoAcknowledge is true, Messages are removed from the Queue when delivered)
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count, bool autoAcknowledge, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, IEnumerable<TrakHoundQueueMessage>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Pull(queue, count, autoAcknowledge, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<TrakHoundQueueMessage>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.MessageQueues.Pull(queue, count, autoAcknowledge, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Pull", requestId, queue) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Subscribes to the specified Queue and returns a Stream of delivered Messages
        /// (when autoAcknowledge is true, Messages are removed from the Queue when delivered, otherwise at most prefetch
        /// unacknowledged Messages are delivered at once, 0 uses the Service's default)
        /// </summary>
        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, ITrakHoundStream<TrakHoundQueueMessage>>, Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Subscribe(queue, autoAcknowledge, prefetch, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<ITrakHoundStream<TrakHoundQueueMessage>>, Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.MessageQueues.Subscribe(queue, autoAcknowledge, prefetch, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Subscribe", requestId, queue) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Publishes a Message with the specified content to the Queue, and returns its Message ID
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            // A fallback Target is only given the content from its start (see RouteContent)
            var routeContent = new RouteContent(content);

            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, TrakHoundQueuePublishResult>, Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>>> serviceCommand = async (serviceRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable<TrakHoundQueuePublishResult>();
                return await serviceRequest.Service.Publish(queue, routeContent.Stream, options, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundQueuePublishResult>, Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>>> routerCommand = async (routerRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable<TrakHoundQueuePublishResult>();
                return await routerRequest.Router.MessageQueues.Publish(queue, routeContent.Stream, options, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Publish", requestId, queue) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Acknowledges the Messages with the specified Delivery IDs so they are removed from the Queue (with a Result for each Delivery ID)
        /// </summary>
        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, string requestId = null, CancellationToken cancellationToken = default)
        {
            return ProcessDeliveries("Acknowledge", queue, requestId, cancellationToken,
                (service) => service.Acknowledge(queue, deliveryIds, cancellationToken),
                (router, id) => router.MessageQueues.Acknowledge(queue, deliveryIds, id, cancellationToken));
        }

        /// <summary>
        /// Rejects the Messages with the specified Delivery IDs (with a Result for each Delivery ID). When requeue is true they are
        /// returned to the Queue to be delivered again, otherwise they are discarded (or dead-lettered)
        /// </summary>
        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, string requestId = null, CancellationToken cancellationToken = default)
        {
            return ProcessDeliveries("Reject", queue, requestId, cancellationToken,
                (service) => service.Reject(queue, deliveryIds, requeue, cancellationToken),
                (router, id) => router.MessageQueues.Reject(queue, deliveryIds, requeue, id, cancellationToken));
        }

        private async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> ProcessDeliveries(
            string operation,
            string queue,
            string requestId,
            CancellationToken cancellationToken,
            Func<ITrakHoundMessageQueueService, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>>> serviceFunction,
            Func<TrakHoundServiceRouter, string, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>>> routerFunction
            )
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, IEnumerable<TrakHoundQueueDeliveryResult>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceFunction(serviceRequest.Service);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<TrakHoundQueueDeliveryResult>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>>> routerCommand = async (routerRequest) =>
            {
                return await routerFunction(routerRequest.Router, routerRequest.Request.Id);
            };


            var request = new RouteRequest(operation, requestId, queue) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Gets the Message counts and rates of the specified Queues (all Queues if none are specified)
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundMessageQueueService, IEnumerable<TrakHoundMessageQueueMetrics>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.GetMetrics(queues, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<TrakHoundMessageQueueMetrics>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.MessageQueues.GetMetrics(queues, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("GetMetrics", requestId, queues) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundMessageQueueRoutes.MessageQueues),
                serviceCommand,
                routerCommand
                );

            return response;
        }
    }
}
