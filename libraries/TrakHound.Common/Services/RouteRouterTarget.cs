// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    class RouteRouterTarget : RouteTarget, IRouteRouterTarget
    {
        public TrakHoundServiceRouter Router { get; set; }


        public RouteRouterTarget(
            TrakHoundServiceRouterConfiguration routerConfiguration,
            TrakHoundServiceRouteConfiguration routeConfiguration,
            TrakHoundServiceTargetConfiguration targetConfiguration
            )
            : base (routerConfiguration, routeConfiguration, targetConfiguration) { }
    }
}
