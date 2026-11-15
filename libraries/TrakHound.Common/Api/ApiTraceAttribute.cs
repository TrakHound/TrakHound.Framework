// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP TRACE Requests
    /// </summary>
    public class ApiTraceAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a TRACE Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiTraceAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.TRACE;
        }

        /// <summary>
        /// Creates a TRACE Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiTraceAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.TRACE;
            Route = route;
            Description = description;
        }
    }
}
