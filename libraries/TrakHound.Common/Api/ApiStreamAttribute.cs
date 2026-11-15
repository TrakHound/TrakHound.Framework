// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Stream Endpoint (WebSocket) that returns a Stream of Responses
    /// </summary>
    public class ApiStreamAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a Stream Endpoint at the Controller's base Route
        /// </summary>
        public ApiStreamAttribute()
        {
            Type = TrakHoundApiEndpointType.Stream;
        }

        /// <summary>
        /// Creates a Stream Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiStreamAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Stream;
            Route = route;
            Description = description;
        }
    }
}
