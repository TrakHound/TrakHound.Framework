// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Logging
{
    /// <summary>
    /// The JSON representation of a <see cref="TrakHoundLogItem"/>
    /// </summary>
    public class TrakHoundLogItemJson
    {
        /// <summary>
        /// The Logger Name (plus an optional sub-name) that wrote the Entry
        /// </summary>
        [JsonPropertyName("sender")]
        public string Sender { get; set; }

        /// <summary>
        /// The severity of the Entry (serialized as a string)
        /// </summary>
        [JsonPropertyName("logLevel")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundLogLevel LogLevel { get; set; }

        /// <summary>
        /// The Message of the Entry
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// An optional Code identifying the type of Entry
        /// </summary>
        [JsonPropertyName("code")]
        public string Code { get; set; }

        /// <summary>
        /// When the Entry was written (Unix Timestamp in nanoseconds)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }


        /// <summary>
        /// Creates an empty Log Entry (for deserialization)
        /// </summary>
        public TrakHoundLogItemJson() { }

        /// <summary>
        /// Creates a JSON Log Entry from the specified Log Entry
        /// </summary>
        public TrakHoundLogItemJson(TrakHoundLogItem item)
        {
            Sender = item.Sender;
            LogLevel = item.LogLevel;
            Message = item.Message;
            Code = item.Code;
            Timestamp = item.Timestamp;
        }

        /// <summary>
        /// Converts to a <see cref="TrakHoundLogItem"/>
        /// </summary>
        public TrakHoundLogItem ToItem()
        {
            var item = new TrakHoundLogItem();
            item.Sender = Sender;
            item.LogLevel = LogLevel;
            item.Message = Message;
            item.Code = Code;
            item.Timestamp = Timestamp;
            return item;
        }
    }
}
