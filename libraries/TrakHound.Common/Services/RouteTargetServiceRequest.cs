// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    class RouteTargetServiceRequest<TService> where TService : ITrakHoundService
    {
        public IRouteTarget Target { get; set; }

        public TService Service { get; set; }

        public RouteRequest Request { get; set; }


        public RouteTargetServiceRequest(IRouteTarget target, TService service, RouteRequest request)
        {
            Target = target;
            Service = service;
            Request = request;
        }
    }

    class RouteTargetDriverRequest<TService, TResult> where TService : ITrakHoundService
    {
        public IRouteTarget Target { get; set; }

        public TService Service { get; set; }

        public RouteRequest Request { get; set; }


        public RouteTargetDriverRequest(IRouteTarget target, TService service, RouteRequest request)
        {
            Target = target;
            Service = service;
            Request = request;
        }
    }
}
