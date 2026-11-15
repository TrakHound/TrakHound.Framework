// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to publish and subscribe to Messages on Topics in the Message Service
    /// </summary>
    public interface ITrakHoundMessageServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Subscribes to Messages published on the specified Topic using the optional Client ID and QoS level
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to Messages published on any of the specified Topics using the optional Client ID and QoS level
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(IEnumerable<string> topics, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to Messages published on the specified Topic, receiving the content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> SubscribeString(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to Messages published on the specified Topic, receiving the content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> SubscribeJson<TOutput>(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Publishes the Stream content as a Message on the Topic. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the byte array content as a Message on the Topic. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string topic, byte[] content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the string content as a Message on the Topic. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string topic, string content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the content serialized as JSON as a Message on the Topic. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string topic, object content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Opens a Publish Stream that publishes each pushed Message on its own Topic with its own Retain and QoS
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TrakHoundMessageResponse>>> PublishStream(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a Publish Stream that publishes each pushed byte array as a Message on the Topic
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<byte[]>>> PublishStreamBytes(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a Publish Stream that publishes each pushed string (as UTF-8) as a Message on the Topic
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<string>>> PublishStreamString(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a Publish Stream that publishes each pushed Item serialized as JSON as a Message on the Topic
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TInput>>> PublishStreamJson<TInput>(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default);
    }
}
