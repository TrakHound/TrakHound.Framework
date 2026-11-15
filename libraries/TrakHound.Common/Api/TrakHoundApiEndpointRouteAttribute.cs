// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Base Attribute used to mark an Api Controller Method as an Endpoint (see ApiGetAttribute, ApiPostAttribute, ApiStreamAttribute, etc.)
    /// </summary>
    public abstract class TrakHoundApiEndpointRouteAttribute : Attribute
    {
        /// <summary>
        /// Whether the Endpoint handles single Requests or a Stream
        /// </summary>
        public TrakHoundApiEndpointType Type { get; init; }

        /// <summary>
        /// The HTTP Method the Endpoint handles (null for Stream Endpoints)
        /// </summary>
        public TrakHoundApiMethod? Method { get; init; }

        /// <summary>
        /// The Route of the Endpoint, relative to the Controller Route (the Controller Route is used when empty)
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// A description of the Endpoint (shown in the Api Information and OpenAPI Document)
        /// </summary>
        public string Description { get; set; }
    }
}
