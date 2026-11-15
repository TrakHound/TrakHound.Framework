// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// The result of Publishing a Message (a class, so more can be returned later without changing Publish)
    /// </summary>
    public class TrakHoundQueuePublishResult
    {
        /// <summary>
        /// The Identifier of the Message: the one set in TrakHoundQueuePublishOptions, or the one the Service created
        /// </summary>
        [JsonPropertyName("messageId")]
        public string MessageId { get; set; }


        public TrakHoundQueuePublishResult() { }

        public TrakHoundQueuePublishResult(string messageId)
        {
            MessageId = messageId;
        }
    }
}
