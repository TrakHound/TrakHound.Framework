// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP DELETE Requests
    /// </summary>
    public class ApiDeleteAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a DELETE Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiDeleteAttribute() 
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.DELETE;
        }

        /// <summary>
        /// Creates a DELETE Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiDeleteAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.DELETE;
            Route = route;
            Description = description;
        }
    }
}
