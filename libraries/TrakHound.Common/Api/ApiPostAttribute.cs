// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP POST Requests
    /// </summary>
    public class ApiPostAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a POST Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiPostAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.POST;
        }

        /// <summary>
        /// Creates a POST Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiPostAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.POST;
            Route = route;
            Description = description;
        }
    }
}
