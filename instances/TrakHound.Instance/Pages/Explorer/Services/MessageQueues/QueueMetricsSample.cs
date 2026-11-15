using TrakHound.Services.MessageQueues;

namespace TrakHound.Instance.Pages.Explorer.Services.MessageQueues
{
    /// <summary>
    /// A point-in-time copy of the values charted for a Queue (or the total of all Queues)
    /// </summary>
    public class QueueMetricsSample
    {
        public DateTime Timestamp { get; set; }

        public double Messages { get; set; }

        public double Ready { get; set; }

        public double Unacknowledged { get; set; }

        public int Consumers { get; set; }

        /// <summary>Messages/s (null when the Service doesn't report rates)</summary>
        public double? PublishRate { get; set; }

        public double? DeliverRate { get; set; }

        public double? AcknowledgeRate { get; set; }

        public long? PublishedTotal { get; set; }

        public long? DeliveredTotal { get; set; }

        public long? AcknowledgedTotal { get; set; }

        // Chart values (Radzen charts need non-nullable values)
        public double Publish => PublishRate ?? 0;

        public double Deliver => DeliverRate ?? 0;

        public double Acknowledge => AcknowledgeRate ?? 0;

        public bool HasRates => PublishRate.HasValue || DeliverRate.HasValue || AcknowledgeRate.HasValue;


        public static QueueMetricsSample Create(TrakHoundMessageQueueMetrics metrics, DateTime timestamp)
        {
            return new QueueMetricsSample
            {
                Timestamp = timestamp,
                Messages = metrics.MessageCount,
                Ready = metrics.ReadyCount,
                Unacknowledged = metrics.UnacknowledgedCount,
                Consumers = metrics.ConsumerCount,
                PublishRate = metrics.PublishRate,
                DeliverRate = metrics.DeliverRate,
                AcknowledgeRate = metrics.AcknowledgeRate,
                PublishedTotal = metrics.PublishedTotal,
                DeliveredTotal = metrics.DeliveredTotal,
                AcknowledgedTotal = metrics.AcknowledgedTotal
            };
        }

        /// <summary>
        /// The total of all Queues (a rate is only totalled when at least one Queue reports it)
        /// </summary>
        public static QueueMetricsSample Total(IEnumerable<TrakHoundMessageQueueMetrics> metrics, DateTime timestamp)
        {
            var items = metrics?.ToList() ?? new List<TrakHoundMessageQueueMetrics>();

            return new QueueMetricsSample
            {
                Timestamp = timestamp,
                Messages = items.Sum(o => o.MessageCount),
                Ready = items.Sum(o => o.ReadyCount),
                Unacknowledged = items.Sum(o => o.UnacknowledgedCount),
                Consumers = items.Sum(o => o.ConsumerCount),
                PublishRate = SumNullable(items.Select(o => o.PublishRate)),
                DeliverRate = SumNullable(items.Select(o => o.DeliverRate)),
                AcknowledgeRate = SumNullable(items.Select(o => o.AcknowledgeRate)),
                PublishedTotal = SumNullable(items.Select(o => o.PublishedTotal)),
                DeliveredTotal = SumNullable(items.Select(o => o.DeliveredTotal)),
                AcknowledgedTotal = SumNullable(items.Select(o => o.AcknowledgedTotal))
            };
        }

        public double GetValue(string property)
        {
            switch (property)
            {
                case nameof(Messages): return Messages;
                case nameof(Ready): return Ready;
                case nameof(Unacknowledged): return Unacknowledged;
                case nameof(Publish): return Publish;
                case nameof(Deliver): return Deliver;
                case nameof(Acknowledge): return Acknowledge;
                default: return 0;
            }
        }


        private static double? SumNullable(IEnumerable<double?> values)
        {
            var reported = values.Where(o => o.HasValue).ToList();
            return reported.Count > 0 ? reported.Sum(o => o.Value) : null;
        }

        private static long? SumNullable(IEnumerable<long?> values)
        {
            var reported = values.Where(o => o.HasValue).ToList();
            return reported.Count > 0 ? reported.Sum(o => o.Value) : null;
        }
    }
}
