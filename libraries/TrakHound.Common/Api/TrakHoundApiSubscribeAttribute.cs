// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    public class TrakHoundApiSubscribeAttribute : TrakHoundApiEndpointRouteAttribute
    {
        public TrakHoundApiSubscribeAttribute() 
        {
            Method = TrakHoundApiRouteType.Subscribe;
        }

        public TrakHoundApiSubscribeAttribute(string route, string description = null)
        {
            Method = TrakHoundApiRouteType.Subscribe;
            Route = route;
            Description = description;
        }
    }
}
