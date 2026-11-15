// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Http.Service
{
    public class HttpMessageQueueService : HttpService, ITrakHoundMessageQueueService
    {
        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.MessageQueues;


        public HttpMessageQueueService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<ITrakHoundStream<TrakHoundQueueMessage>>(NotConfiguredMessage);

            // Stream is a WebSocket connection to the remote Instance
            return Forward(await Client.Services.MessageQueues.Subscribe(queue, autoAcknowledge, prefetch, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count, bool autoAcknowledge, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundQueueMessage>>(NotConfiguredMessage);

            return Forward(await Client.Services.MessageQueues.Pull(queue, count, autoAcknowledge, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundQueueDeliveryResult>>(NotConfiguredMessage);

            return Forward(await Client.Services.MessageQueues.Acknowledge(queue, deliveryIds, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundQueueDeliveryResult>>(NotConfiguredMessage);

            return Forward(await Client.Services.MessageQueues.Reject(queue, deliveryIds, requeue, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundQueuePublishResult>(NotConfiguredMessage);

            return Forward(await Client.Services.MessageQueues.Publish(queue, content, options, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundMessageQueueMetrics>>(NotConfiguredMessage);

            return Forward(await Client.Services.MessageQueues.GetMetrics(queues, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
