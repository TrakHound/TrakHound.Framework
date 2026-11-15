// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    public class TrakHoundApiQueryAttribute : TrakHoundApiEndpointRouteAttribute
    {
        public TrakHoundApiQueryAttribute() 
        {
            Method = TrakHoundApiRouteType.Query;
        }

        public TrakHoundApiQueryAttribute(string route, string description = null)
        {
            Method = TrakHoundApiRouteType.Query;
            Route = route;
            Description = description;
        }
    }
}
