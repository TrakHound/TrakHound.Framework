// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using System.Collections.Concurrent;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Memory.Service
{
    /// <summary>
    /// In memory Message Queues: each Message is delivered to one consumer (by Pull or Subscribe) and stays on its Queue
    /// until it is Acknowledged or Rejected. Nothing is persisted.
    /// </summary>
    public class MemoryMessageQueueService :
        MemoryService,
        ITrakHoundMessageQueueService
    {
        private const int _defaultMaxQueueLength = 10000;
        private const int _defaultPrefetch = 100;

        private readonly ConcurrentDictionary<string, MemoryQueue> _queues = new ConcurrentDictionary<string, MemoryQueue>(StringComparer.Ordinal);
        private readonly string _serviceId = Guid.NewGuid().ToString(); // Part of each Delivery ID, so IDs from a replaced Service aren't accepted
        private readonly int _maxQueueLength;
        private readonly int _prefetch;
        private long _deliverySequence;
        private volatile bool _disposed;


        public MemoryMessageQueueService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            var maxQueueLength = configuration?.GetParameter<int>("maxQueueLength") ?? 0;
            var prefetch = configuration?.GetParameter<int>("prefetch") ?? 0;
            _maxQueueLength = maxQueueLength > 0 ? maxQueueLength : _defaultMaxQueueLength;
            _prefetch = prefetch > 0 ? prefetch : _defaultPrefetch;
        }

        protected override void OnDisposed()
        {
            _disposed = true;

            // The Service is replaced when its Configuration changes, so Subscriptions can't be left attached to this instance
            foreach (var queue in _queues.Values)
            {
                foreach (var consumer in queue.GetConsumers()) consumer.Dispose();
            }
            _queues.Clear();
        }


        public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var response = new TrakHoundServiceAvailabilityResponse();
            response.IsAvailable = !_disposed;

            return Task.FromResult(Ok(response));
        }


        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(queue)) return Task.FromResult(BadRequest<ITrakHoundStream<TrakHoundQueueMessage>>("Queue is required"));
            if (_disposed) return Task.FromResult(NotAvailable<ITrakHoundStream<TrakHoundQueueMessage>>("Service Disposed"));

            var consumer = new MemoryQueueConsumer(GetQueue(queue), autoAcknowledge, prefetch > 0 ? prefetch : _prefetch, (level, message) => Log(level, message));
            consumer.Attach(); // Messages are raised once the caller attaches its handlers and calls Start()

            Log(TrakHoundLogLevel.Debug, $"Memory Message Queues : Subscribed : ({queue}) : {consumer.Id}");

            return Task.FromResult(Ok<ITrakHoundStream<TrakHoundQueueMessage>>(consumer));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count, bool autoAcknowledge, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(queue)) return Task.FromResult(BadRequest<IEnumerable<TrakHoundQueueMessage>>("Queue is required"));
            if (_disposed) return Task.FromResult(NotAvailable<IEnumerable<TrakHoundQueueMessage>>("Service Disposed"));

            var messages = count > 0 ? GetQueue(queue).Pull(count, autoAcknowledge) : new List<TrakHoundQueueMessage>();
            return Task.FromResult(Ok<IEnumerable<TrakHoundQueueMessage>>(messages));
        }

        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || content == null) return BadRequest<TrakHoundQueuePublishResult>("Queue and Content are required");
            if (_disposed) return NotAvailable<TrakHoundQueuePublishResult>("Service Disposed");

            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
                await content.CopyToAsync(memoryStream, cancellationToken);
                bytes = memoryStream.ToArray();
            }

            var entry = new MemoryQueueEntry
            {
                MessageId = !string.IsNullOrEmpty(options?.MessageId) ? options.MessageId : Guid.NewGuid().ToString(),
                ContentType = options?.ContentType,
                Headers = options?.Headers != null ? new Dictionary<string, string>(options.Headers) : null,
                Content = bytes,
                PublishedTimestamp = UnixDateTime.Now
            };

            if (!GetQueue(queue).Publish(entry, _maxQueueLength))
            {
                return Conflict<TrakHoundQueuePublishResult>($"Queue ({queue}) is full ({_maxQueueLength} Messages)");
            }

            return Ok(new TrakHoundQueuePublishResult(entry.MessageId));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ProcessDeliveries(queue, deliveryIds, (memoryQueue, deliveryId) => memoryQueue.Acknowledge(deliveryId)));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ProcessDeliveries(queue, deliveryIds, (memoryQueue, deliveryId) => memoryQueue.Reject(deliveryId, requeue)));
        }

        private TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>> ProcessDeliveries(string queue, IEnumerable<string> deliveryIds, Func<MemoryQueue, string, bool> action)
        {
            if (string.IsNullOrEmpty(queue) || deliveryIds.IsNullOrEmpty()) return BadRequest<IEnumerable<TrakHoundQueueDeliveryResult>>("Queue and Delivery IDs are required");

            _queues.TryGetValue(queue, out var memoryQueue);

            var results = new List<TrakHoundQueueDeliveryResult>();
            foreach (var deliveryId in deliveryIds)
            {
                if (memoryQueue != null && deliveryId != null && action(memoryQueue, deliveryId))
                {
                    results.Add(new TrakHoundQueueDeliveryResult(deliveryId, true));
                }
                else
                {
                    results.Add(new TrakHoundQueueDeliveryResult(deliveryId, false, "Delivery ID Not Found (it was already Acknowledged or Rejected, or its Subscription closed and it was returned to the Queue)"));
                }
            }

            return Ok<IEnumerable<TrakHoundQueueDeliveryResult>>(results);
        }


        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, CancellationToken cancellationToken = default)
        {
            var requested = queues?.Where(o => !string.IsNullOrEmpty(o)).ToHashSet(StringComparer.Ordinal);
            if (requested != null && requested.Count < 1) requested = null;

            var timestamp = UnixDateTime.Now;
            var metrics = _queues.Values
                .Where(o => requested == null || requested.Contains(o.Name))
                .Select(o => o.GetMetrics(timestamp))
                .OrderBy(o => o.Queue, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(Ok<IEnumerable<TrakHoundMessageQueueMetrics>>(metrics));
        }


        private MemoryQueue GetQueue(string name) => _queues.GetOrAdd(name, o => new MemoryQueue(o, () => $"{_serviceId}:{Interlocked.Increment(ref _deliverySequence)}"));
    }
}
