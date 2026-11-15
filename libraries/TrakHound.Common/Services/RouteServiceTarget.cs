// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services
{
    class RouteServiceTarget : RouteTarget, IRouteServiceTarget
    {
        // The Services and the Services of each type are replaced together (when the Target is remapped),
        // so a request never reads a type list built from a previous set of Services
        private volatile ServiceSet _serviceSet = new ServiceSet(null);


        public IEnumerable<ITrakHoundService> Services
        {
            get => _serviceSet.Services;
            set => _serviceSet = new ServiceSet(value);
        }


        public RouteServiceTarget(
            TrakHoundServiceRouterConfiguration routerConfiguration,
            TrakHoundServiceRouteConfiguration routeConfiguration,
            TrakHoundServiceTargetConfiguration targetConfiguration
            ) 
            : base(routerConfiguration, routeConfiguration, targetConfiguration) { }


        public TService GetService<TService>() where TService : ITrakHoundService
        {
            // Get the Service that matches the specified ITrakHoundService (TService)
            var services = GetServiceArray<TService>();
            return services.Length > 0 ? services[0] : default;
        }

        public IEnumerable<TService> GetServices<TService>() where TService : ITrakHoundService
        {
            // Get the Service(s) that matches the specified ITrakHoundService (TService)
            return GetServiceArray<TService>();
        }

        private TService[] GetServiceArray<TService>() where TService : ITrakHoundService
        {
            // Built once per type (and per set of Services) since this runs on every request
            var serviceSet = _serviceSet;
            return (TService[])serviceSet.ByType.GetOrAdd(typeof(TService), static (_, services) =>
            {
                return services.IsNullOrEmpty() ? Array.Empty<TService>() : services.OfType<TService>().ToArray();
            }, serviceSet.Services);
        }


        private class ServiceSet
        {
            public readonly IEnumerable<ITrakHoundService> Services;
            public readonly ConcurrentDictionary<Type, Array> ByType = new ConcurrentDictionary<Type, Array>();

            public ServiceSet(IEnumerable<ITrakHoundService> services)
            {
                // Copied so a later change to the source collection can't make the cached lists stale
                Services = services?.ToList();
            }
        }
    }
}
