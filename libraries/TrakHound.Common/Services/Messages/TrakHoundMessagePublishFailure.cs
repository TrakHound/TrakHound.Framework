// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// A Message that a Publish Stream could not publish
    /// </summary>
    public class TrakHoundMessagePublishFailure
    {
        /// <summary>
        /// The Topic the Message was published to (null if the Message had no Topic)
        /// </summary>
        [JsonPropertyName("topic")]
        public string Topic { get; set; }

        /// <summary>
        /// The HTTP style Status Code of the failure (ex. 400 for an invalid Message, 403 when publishing isn't allowed, 503 when the Service is unavailable)
        /// </summary>
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Why the Message wasn't published
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }


        /// <summary>
        /// Creates a new Publish Failure
        /// </summary>
        public TrakHoundMessagePublishFailure() { }

        /// <summary>
        /// Creates a new Publish Failure for the Topic
        /// </summary>
        public TrakHoundMessagePublishFailure(string topic, int statusCode, string message)
        {
            Topic = topic;
            StatusCode = statusCode;
            Message = message;
        }


        /// <summary>
        /// Creates a Publish Failure from an unsuccessful Service Response (null if the Response was successful)
        /// </summary>
        public static TrakHoundMessagePublishFailure FromResponse(string topic, TrakHoundServiceResponse response)
        {
            if (response.Success) return null;

            return new TrakHoundMessagePublishFailure(topic, response.StatusCode, response.Message);
        }
    }
}
