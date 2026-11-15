// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    class RouteTargetRouterRequest
    {
        public IRouteTarget Target { get; set; }

        public TrakHoundServiceRouter Router { get; set; }

        public RouteRequest Request { get; set; }


        public RouteTargetRouterRequest(IRouteTarget target, TrakHoundServiceRouter router, RouteRequest request)
        {
            Target = target;
            Router = router;
            Request = request;
        }
    }

    class RouteTargetRouterRequest<TResult>
    {
        public IRouteTarget Target { get; set; }

        public TrakHoundServiceRouter Router { get; set; }

        public RouteRequest Request { get; set; }


        public RouteTargetRouterRequest(IRouteTarget target, TrakHoundServiceRouter router, RouteRequest request)
        {
            Target = target;
            Router = router;
            Request = request;
        }
    }
}
