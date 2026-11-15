// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Marks an Api Controller Method as a Request Endpoint that handles HTTP PATCH Requests
    /// </summary>
    public class ApiPatchAttribute : TrakHoundApiEndpointRouteAttribute
    {
        /// <summary>
        /// Creates a PATCH Request Endpoint at the Controller's base Route
        /// </summary>
        public ApiPatchAttribute()
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.PATCH;
        }

        /// <summary>
        /// Creates a PATCH Request Endpoint at the specified Route (relative to the Controller Route) with an optional Description
        /// </summary>
        public ApiPatchAttribute(string route, string description = null)
        {
            Type = TrakHoundApiEndpointType.Request;
            Method = TrakHoundApiMethod.PATCH;
            Route = route;
            Description = description;
        }
    }
}
