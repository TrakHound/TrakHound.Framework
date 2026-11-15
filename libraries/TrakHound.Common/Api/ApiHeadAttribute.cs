// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP HEAD Requests
    /// </summary>
    public class ApiHeadAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a HEAD Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiHeadAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.HEAD;
        }

        /// <summary>
        /// Creates a HEAD Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiHeadAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.HEAD;
            Route = route;
            Description = description;
        }
    }
}
