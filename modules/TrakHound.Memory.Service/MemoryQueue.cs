// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Services.MessageQueues;

namespace TrakHound.Memory.Service
{
    /// <summary>
    /// A Message waiting on a Queue (or delivered and not Acknowledged yet)
    /// </summary>
    internal sealed class MemoryQueueEntry
    {
        public string MessageId { get; init; }

        public string ContentType { get; init; }

        public Dictionary<string, string> Headers { get; init; }

        public byte[] Content { get; init; }

        public long PublishedTimestamp { get; init; }

        /// <summary>
        /// Set when the Message is returned to the Queue after a delivery
        /// </summary>
        public bool Redelivered { get; set; }
    }

    /// <summary>
    /// One Queue: its Ready Messages, its delivered Messages that are waiting to be Acknowledged, and its Subscriptions.
    /// Everything is changed while holding _lock.
    /// </summary>
    internal sealed class MemoryQueue
    {
        // Pulled Messages belong to the Service (like RabbitMQ's shared Pull Channel) until they are Acknowledged or Rejected
        private const string PullOwner = "pull";

        private sealed class Delivery
        {
            public MemoryQueueEntry Entry { get; init; }

            public string Owner { get; init; }

            public long Sequence { get; init; }
        }

        private readonly object _lock = new object();
        private readonly LinkedList<MemoryQueueEntry> _ready = new LinkedList<MemoryQueueEntry>();
        private readonly Dictionary<string, Delivery> _unacknowledged = new Dictionary<string, Delivery>(StringComparer.Ordinal);
        private readonly List<MemoryQueueConsumer> _consumers = new List<MemoryQueueConsumer>();
        private readonly Func<string> _createDeliveryId;
        private long _sequence;
        private int _nextConsumer;
        private long _publishedTotal;
        private long _deliveredTotal;
        private long _acknowledgedTotal;


        public string Name { get; }


        public MemoryQueue(string name, Func<string> createDeliveryId)
        {
            Name = name;
            _createDeliveryId = createDeliveryId;
        }


        /// <summary>
        /// Adds the Message to the Queue. Returns false if the Queue is full
        /// </summary>
        public bool Publish(MemoryQueueEntry entry, int maxLength)
        {
            lock (_lock)
            {
                if (_ready.Count + _unacknowledged.Count >= maxLength) return false;

                _ready.AddLast(entry);
                _publishedTotal++;
                Dispatch();
                return true;
            }
        }

        public List<TrakHoundQueueMessage> Pull(int count, bool autoAcknowledge)
        {
            var results = new List<TrakHoundQueueMessage>();

            lock (_lock)
            {
                while (results.Count < count && _ready.Count > 0)
                {
                    var entry = _ready.First.Value;
                    _ready.RemoveFirst();
                    results.Add(Deliver(entry, autoAcknowledge ? null : PullOwner));
                }
            }

            return results;
        }

        public bool Acknowledge(string deliveryId)
        {
            lock (_lock)
            {
                if (!Remove(deliveryId, out _)) return false;

                _acknowledgedTotal++;
                Dispatch(); // The Subscription it was delivered to may have room for another Message
                return true;
            }
        }

        public bool Reject(string deliveryId, bool requeue)
        {
            lock (_lock)
            {
                if (!Remove(deliveryId, out var delivery)) return false;

                if (requeue)
                {
                    delivery.Entry.Redelivered = true;
                    _ready.AddFirst(delivery.Entry);
                }

                Dispatch();
                return true;
            }
        }


        public void AddConsumer(MemoryQueueConsumer consumer)
        {
            lock (_lock)
            {
                if (!_consumers.Contains(consumer)) _consumers.Add(consumer);
                Dispatch();
            }
        }

        /// <summary>
        /// Removes the Subscription and returns the Messages it hadn't Acknowledged to the front of the Queue (in order)
        /// </summary>
        public void RemoveConsumer(MemoryQueueConsumer consumer)
        {
            lock (_lock)
            {
                if (!_consumers.Remove(consumer)) return;

                var returned = _unacknowledged.Where(o => o.Value.Owner == consumer.Id).OrderByDescending(o => o.Value.Sequence).ToList();
                foreach (var delivery in returned)
                {
                    _unacknowledged.Remove(delivery.Key);
                    delivery.Value.Entry.Redelivered = true;
                    _ready.AddFirst(delivery.Value.Entry);
                }

                consumer.Unacknowledged = 0;
                Dispatch();
            }
        }

        public List<MemoryQueueConsumer> GetConsumers()
        {
            lock (_lock) return _consumers.ToList();
        }


        public TrakHoundMessageQueueMetrics GetMetrics(long timestamp)
        {
            lock (_lock)
            {
                return new TrakHoundMessageQueueMetrics
                {
                    Queue = Name,
                    MessageCount = _ready.Count + _unacknowledged.Count,
                    ReadyCount = _ready.Count,
                    UnacknowledgedCount = _unacknowledged.Count,
                    ConsumerCount = _consumers.Count,
                    PublishedTotal = _publishedTotal,
                    DeliveredTotal = _deliveredTotal,
                    AcknowledgedTotal = _acknowledgedTotal,
                    Timestamp = timestamp
                };
            }
        }


        /// <summary>
        /// Sends Ready Messages to the Subscriptions that have room for them (in turn). Called while holding _lock
        /// </summary>
        private void Dispatch()
        {
            while (_ready.Count > 0 && _consumers.Count > 0)
            {
                MemoryQueueConsumer consumer = null;
                for (var i = 0; i < _consumers.Count; i++)
                {
                    var candidate = _consumers[(_nextConsumer + i) % _consumers.Count];
                    if (candidate.AutoAcknowledge || candidate.Unacknowledged < candidate.Prefetch)
                    {
                        consumer = candidate;
                        _nextConsumer = (_nextConsumer + i + 1) % _consumers.Count;
                        break;
                    }
                }
                if (consumer == null) return; // Every Subscription has as many unacknowledged Messages as its prefetch

                var entry = _ready.First.Value;
                _ready.RemoveFirst();

                if (!consumer.AutoAcknowledge) consumer.Unacknowledged++;
                consumer.Enqueue(Deliver(entry, consumer.AutoAcknowledge ? null : consumer.Id));
            }
        }

        /// <summary>
        /// Creates the delivered Message (and holds it until Acknowledged, unless owner is null). Called while holding _lock
        /// </summary>
        private TrakHoundQueueMessage Deliver(MemoryQueueEntry entry, string owner)
        {
            var deliveryId = _createDeliveryId();
            if (owner != null) _unacknowledged[deliveryId] = new Delivery { Entry = entry, Owner = owner, Sequence = ++_sequence };
            _deliveredTotal++;

            return new TrakHoundQueueMessage
            {
                Queue = Name,
                DeliveryId = deliveryId,
                MessageId = entry.MessageId,
                ContentType = entry.ContentType,
                Headers = entry.Headers != null ? new Dictionary<string, string>(entry.Headers) : null,
                Content = entry.Content,
                Redelivered = entry.Redelivered,
                PublishedTimestamp = entry.PublishedTimestamp,
                ReceivedTimestamp = UnixDateTime.Now
            };
        }

        private bool Remove(string deliveryId, out Delivery delivery)
        {
            if (!_unacknowledged.Remove(deliveryId, out delivery)) return false;

            if (delivery.Owner != PullOwner)
            {
                var owner = delivery.Owner;
                var consumer = _consumers.FirstOrDefault(o => o.Id == owner);
                if (consumer != null && consumer.Unacknowledged > 0) consumer.Unacknowledged--;
            }

            return true;
        }
    }
}
