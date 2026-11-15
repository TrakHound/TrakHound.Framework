// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// The current Message counts and Message rates of a Queue. Rates are Messages per second and are null when the
    /// Service can't read them from the Broker (ex. the RabbitMQ management plugin is not available)
    /// </summary>
    public class TrakHoundMessageQueueMetrics
    {
        /// <summary>
        /// The name of the Queue
        /// </summary>
        [JsonPropertyName("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// Messages on the Queue (Ready + Unacknowledged)
        /// </summary>
        [JsonPropertyName("messageCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long MessageCount { get; set; }

        /// <summary>
        /// Messages waiting to be delivered
        /// </summary>
        [JsonPropertyName("readyCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long ReadyCount { get; set; }

        /// <summary>
        /// Messages delivered to a Consumer that have not been Acknowledged yet
        /// </summary>
        [JsonPropertyName("unacknowledgedCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long UnacknowledgedCount { get; set; }

        /// <summary>
        /// The number of Consumers subscribed to the Queue
        /// </summary>
        [JsonPropertyName("consumerCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ConsumerCount { get; set; }

        /// <summary>
        /// Messages Published to the Queue (Messages/s)
        /// </summary>
        [JsonPropertyName("publishRate")]
        public double? PublishRate { get; set; }

        /// <summary>
        /// Messages delivered to Consumers or Pulled (Messages/s)
        /// </summary>
        [JsonPropertyName("deliverRate")]
        public double? DeliverRate { get; set; }

        /// <summary>
        /// Messages Acknowledged (Messages/s)
        /// </summary>
        [JsonPropertyName("acknowledgeRate")]
        public double? AcknowledgeRate { get; set; }

        /// <summary>
        /// Messages delivered again after being Rejected or not Acknowledged (Messages/s)
        /// </summary>
        [JsonPropertyName("redeliverRate")]
        public double? RedeliverRate { get; set; }

        /// <summary>
        /// Total Messages Published to the Queue (null if not available)
        /// </summary>
        [JsonPropertyName("publishedTotal")]
        public long? PublishedTotal { get; set; }

        /// <summary>
        /// Total Messages delivered to Consumers or Pulled (null if not available)
        /// </summary>
        [JsonPropertyName("deliveredTotal")]
        public long? DeliveredTotal { get; set; }

        /// <summary>
        /// Total Messages Acknowledged (null if not available)
        /// </summary>
        [JsonPropertyName("acknowledgedTotal")]
        public long? AcknowledgedTotal { get; set; }

        /// <summary>
        /// When the Metrics were read (Unix nanoseconds)
        /// </summary>
        [JsonPropertyName("timestamp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long Timestamp { get; set; }
    }
}
