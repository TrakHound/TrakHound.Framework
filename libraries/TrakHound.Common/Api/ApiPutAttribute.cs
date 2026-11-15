// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP PUT Requests
    /// </summary>
    public class ApiPutAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a PUT Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiPutAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.PUT;
        }

        /// <summary>
        /// Creates a PUT Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiPutAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.PUT;
            Route = route;
            Description = description;
        }
    }
}
