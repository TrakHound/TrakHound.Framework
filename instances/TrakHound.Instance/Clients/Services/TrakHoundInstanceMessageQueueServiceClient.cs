// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceMessageQueueServiceClient : ITrakHoundMessageQueueServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceMessageQueueServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.MessageQueues.CheckAvailability(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count = 1, bool autoAcknowledge = false, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue)) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>.BadRequest("Queue is required");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.MessageQueues.Pull(queue, count, autoAcknowledge, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge = false, int prefetch = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue)) return TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>.BadRequest("Queue is required");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.MessageQueues.Subscribe(queue, autoAcknowledge, prefetch, cancellationToken: cancellationToken);
        }


        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || content == null) return TrakHoundServiceResponse<TrakHoundQueuePublishResult>.BadRequest("Queue and Content are required");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<TrakHoundQueuePublishResult>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.MessageQueues.Publish(queue, content, options, cancellationToken: cancellationToken);
        }

        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, byte[] content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromBytes(content), options, routerId, cancellationToken);
        }

        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, string content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromString(content), TrakHoundQueueContent.WithDefaultContentType(options, TrakHoundQueueContent.TextContentType), routerId, cancellationToken);
        }

        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, object content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromJson(content), TrakHoundQueueContent.WithDefaultContentType(options, TrakHoundQueueContent.JsonContentType), routerId, cancellationToken);
        }


        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || deliveryIds.IsNullOrEmpty()) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>.BadRequest("Queue and Delivery IDs are required");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.MessageQueues.Acknowledge(queue, deliveryIds, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || deliveryIds.IsNullOrEmpty()) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>.BadRequest("Queue and Delivery IDs are required");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.MessageQueues.Reject(queue, deliveryIds, requeue, cancellationToken: cancellationToken);
        }


        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.MessageQueues.GetMetrics(queues, cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }
    }
}
