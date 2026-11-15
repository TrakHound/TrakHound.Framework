// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// The type of an Api Endpoint
    /// </summary>
    public enum TrakHoundApiEndpointType
    {
        /// <summary>
        /// Handles a single Request and returns a single Response
        /// </summary>
        Request,
        /// <summary>
        /// Returns a Stream of Responses over a persistent connection (WebSocket)
        /// </summary>
        Stream
    }
}
