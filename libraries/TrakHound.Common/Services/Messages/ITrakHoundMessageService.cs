// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// Service (implemented by a Driver) that publishes and subscribes to Messages on Topics
    /// </summary>
    public interface ITrakHoundMessageService : ITrakHoundService
    {
        /// <summary>
        /// Subscribes to Messages published on any of the Topics (Topic Filters) using the Client ID and QoS level
        /// </summary>
        Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string clientId, IEnumerable<string> topics, int qos, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the content as a Message on the Topic. If retain is true, the Message is kept as the Topic's Retained Message
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, CancellationToken cancellationToken = default);
    }
}
