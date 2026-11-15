// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP CONNECT Requests
    /// </summary>
    public class ApiConnectAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a CONNECT Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiConnectAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.CONNECT;
        }

        /// <summary>
        /// Creates a CONNECT Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiConnectAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.CONNECT;
            Route = route;
            Description = description;
        }
    }
}
