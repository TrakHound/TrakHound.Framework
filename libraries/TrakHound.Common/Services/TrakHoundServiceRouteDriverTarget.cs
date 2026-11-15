// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services
{
    class TrakHoundServiceRouteDriverTarget : RouteTarget, IRouteServiceTarget
    {
        public IEnumerable<ITrakHoundService> Services { get; set; }


        public TrakHoundServiceRouteDriverTarget(
            TrakHoundServiceRouterConfiguration routerConfiguration,
            TrakHoundServiceRouteConfiguration routeConfiguration,
            TrakHoundServiceTargetConfiguration targetConfiguration
            ) 
            : base(routerConfiguration, routeConfiguration, targetConfiguration) { }


        public TDriver GetDriver<TDriver>() where TDriver : ITrakHoundService
        {
            if (!Services.IsNullOrEmpty())
            {
                // Get the Driver that matches the specified IApiDriver (TDriver)
                return (TDriver)Services.FirstOrDefault(x => typeof(TDriver).IsAssignableFrom(x.GetType()));
            }

            return default;
        }

        public IEnumerable<TDriver> GetServices<TDriver>() where TDriver : ITrakHoundService
        {
            var x = new List<TDriver>();

            if (!Services.IsNullOrEmpty())
            {
                // Get the Driver(s) that matches the specified ITrakHoundDriver (TDriver)
                var y = Services.Where(x => typeof(TDriver).IsAssignableFrom(x.GetType())).OfType<TDriver>();
                if (!y.IsNullOrEmpty()) x.AddRange(y);
            }

            return x;
        }
    }
}
