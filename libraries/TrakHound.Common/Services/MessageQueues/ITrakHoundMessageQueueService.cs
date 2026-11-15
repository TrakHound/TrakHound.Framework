// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// A Service that publishes Messages to Queues and delivers them to Consumers (by Subscribe or Pull)
    /// </summary>
    public interface ITrakHoundMessageQueueService : ITrakHoundService
    {
        /// <summary>
        /// Subscribes to the specified Queue and returns a Stream of delivered Messages. When autoAcknowledge is true, Messages are
        /// removed from the Queue as they are delivered. Otherwise each must be Acknowledged or Rejected, and at most prefetch
        /// unacknowledged Messages are delivered at once (0 uses the Service's default)
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pulls up to the specified number of Messages from the Queue (an empty Queue returns an empty list).
        /// When autoAcknowledge is true, Messages are removed from the Queue as they are delivered
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count, bool autoAcknowledge, CancellationToken cancellationToken = default);

        /// <summary>
        /// Acknowledges the Messages with the specified Delivery IDs so they are removed from the Queue. Returns a Result for each Delivery ID
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rejects the Messages with the specified Delivery IDs. When requeue is true they are returned to the Queue to be delivered again,
        /// otherwise they are discarded (or dead-lettered, if the Queue is configured for it). Returns a Result for each Delivery ID
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a Message with the specified content (and optional properties) to the Queue, and returns its Message ID
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the Message counts and rates of the specified Queues (all Queues if none are specified).
        /// Implemented by default (as Route Not Configured, so Routers try the next Target) so Services built before Metrics were added still load
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>.RouteNotConfigured("Metrics are not supported by this Service"));
        }
    }
}
