// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to publish, pull, and subscribe to Messages in the Message Queue Service
    /// </summary>
    public interface ITrakHoundMessageQueueServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Pulls up to the specified count of Messages from the Queue (an empty Queue returns an empty list). If autoAcknowledge is true,
        /// the Messages are removed from the Queue when pulled. Otherwise each must be Acknowledged or Rejected
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count = 1, bool autoAcknowledge = false, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to Messages delivered from the Queue. If autoAcknowledge is true, each Message is removed from the Queue when delivered.
        /// Otherwise each must be Acknowledged or Rejected, and at most prefetch unacknowledged Messages are delivered at once (0 uses the Service's default)
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge = false, int prefetch = 0, string routerId = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Publishes the Stream content as a Message to the Queue, and returns its Message ID
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the byte array content as a Message to the Queue, and returns its Message ID
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, byte[] content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the string content (UTF-8) as a Message to the Queue, and returns its Message ID. The Content Type is text/plain unless set in options
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, string content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the content serialized as JSON as a Message to the Queue, and returns its Message ID. The Content Type is application/json unless set in options
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, object content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Acknowledges the Messages with the specified Delivery IDs so they are removed from the Queue. Returns a Result for each Delivery ID
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rejects the Messages with the specified Delivery IDs. When requeue is true they are delivered again, otherwise they are
        /// discarded (or dead-lettered). Returns a Result for each Delivery ID
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, string routerId = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Gets the Message counts and rates of the specified Queues (all Queues if none are specified)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, string routerId = null, CancellationToken cancellationToken = default);
    }
}
