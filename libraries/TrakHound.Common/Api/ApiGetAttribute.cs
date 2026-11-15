// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP GET Requests
    /// </summary>
    public class ApiGetAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a GET Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiGetAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.GET;
        }

        /// <summary>
        /// Creates a GET Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiGetAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.GET;
            Route = route;
            Description = description;
        }
    }
}
