// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// A Message delivered from a Queue (by Pull or Subscribe). Serialized as-is by the System API (the Content is Base64 encoded in JSON)
    /// </summary>
    public class TrakHoundQueueMessage
    {
        /// <summary>
        /// True if the Queue, DeliveryId and Content are set
        /// </summary>
        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(Queue) && !string.IsNullOrEmpty(DeliveryId) && Content != null;

        /// <summary>
        /// The name of the Queue the Message was delivered from
        /// </summary>
        [JsonPropertyName("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// The Identifier used to Acknowledge or Reject this delivery of the Message
        /// </summary>
        [JsonPropertyName("deliveryId")]
        public string DeliveryId { get; set; }

        /// <summary>
        /// The Identifier of the Message, as set when it was Published (null if not set)
        /// </summary>
        [JsonPropertyName("messageId")]
        public string MessageId { get; set; }

        /// <summary>
        /// The media type of the Content, as set when it was Published (null if not set)
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// The Headers set when the Message was Published (null if none)
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// The content of the Message
        /// </summary>
        [JsonPropertyName("content")]
        public byte[] Content { get; set; }

        /// <summary>
        /// True if the Message was delivered before and was Rejected or not Acknowledged (ex. its Consumer disconnected)
        /// </summary>
        [JsonPropertyName("redelivered")]
        public bool Redelivered { get; set; }

        /// <summary>
        /// When the Message was Published (Unix nanoseconds, 0 if not known)
        /// </summary>
        [JsonPropertyName("publishedTimestamp")]
        public long PublishedTimestamp { get; set; }

        /// <summary>
        /// When the Message was received from the Broker (Unix nanoseconds)
        /// </summary>
        [JsonPropertyName("receivedTimestamp")]
        public long ReceivedTimestamp { get; set; }


        /// <summary>
        /// Reads the Content as a UTF-8 string (null if there is no Content)
        /// </summary>
        public string GetContentUtf8String() => Content != null ? Encoding.UTF8.GetString(Content) : null;

        /// <summary>
        /// Deserializes the Content from JSON to TOutput
        /// </summary>
        public TOutput GetJsonContentObject<TOutput>() => Json.Convert<TOutput>(GetContentUtf8String());
    }
}
