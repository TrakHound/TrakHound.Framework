// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP OPTIONS Requests
    /// </summary>
    public class ApiOptionsAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a OPTIONS Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiOptionsAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.OPTIONS;
        }

        /// <summary>
        /// Creates a OPTIONS Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiOptionsAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.OPTIONS;
            Route = route;
            Description = description;
        }
    }
}
