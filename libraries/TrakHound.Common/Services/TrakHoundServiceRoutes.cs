// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TrakHound.Services
{
    /// <summary>
    /// Maps Service interfaces to the names of the Routes they handle (ex. ITrakHoundBlobService = "Blobs")
    /// </summary>
    public static class TrakHoundServiceRoutes
    {
        /// <summary>
        /// A Route that matches all Routes
        /// </summary>
        public const string Wildcard = "*";

        /// <summary>
        /// The names of every Route (ex. "Blobs")
        /// </summary>
        public static IReadOnlyList<string> Names { get; } = new[]
        {
            Blobs.TrakHoundBlobRoutes.Blobs,
            KeyValues.TrakHoundKeyValueRoutes.KeyValues,
            Messages.TrakHoundMessageRoutes.Messages,
            MessageQueues.TrakHoundMessageQueueRoutes.MessageQueues,
            Tables.TrakHoundTableRoutes.Tables,
            TimeSeries.TrakHoundTimeSeriesRoutes.TimeSeries
        };

        private static Dictionary<Type, string> _routeCache;
        private static object _lock = new object();


        /// <summary>
        /// Gets the Route name for each Service interface
        /// </summary>
        public static Dictionary<Type, string> GetRoutes()
        {
            lock (_lock)
            {
                if (_routeCache == null)
                {
                    var routes = new Dictionary<Type, string>();

                    foreach (var route in Blobs.TrakHoundBlobRoutes._routes) routes.Add(route.Key, route.Value);
                    foreach (var route in KeyValues.TrakHoundKeyValueRoutes._routes) routes.Add(route.Key, route.Value);
                    foreach (var route in Messages.TrakHoundMessageRoutes._routes) routes.Add(route.Key, route.Value);
                    foreach (var route in MessageQueues.TrakHoundMessageQueueRoutes._routes) routes.Add(route.Key, route.Value);
                    foreach (var route in Tables.TrakHoundTableRoutes._routes) routes.Add(route.Key, route.Value);
                    foreach (var route in TimeSeries.TrakHoundTimeSeriesRoutes._routes) routes.Add(route.Key, route.Value);

                    _routeCache = routes;
                }

                return _routeCache;
            }
        }


        /// <summary>
        /// Gets the Route for the specified type or the first of its interfaces that has one (null if none)
        /// </summary>
        public static string Get(Type type)
        {
            if (type != null)
            {
                GetRoutes().TryGetValue(type, out var route);
                if (route != null) return route;

                var interfaceTypes = type.GetInterfaces();
                if (!interfaceTypes.IsNullOrEmpty())
                {
                    foreach (var interfaceType in interfaceTypes)
                    {
                        GetRoutes().TryGetValue(interfaceType, out route);
                        if (route != null) return route;
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the Routes for the specified types and their interfaces
        /// </summary>
        public static IEnumerable<string> Get(IEnumerable<Type> types)
        {
            var routes = new List<string>();

            if (!types.IsNullOrEmpty())
            {
                foreach (var type in types)
                {
                    GetRoutes().TryGetValue(type, out var route);
                    if (route != null) routes.Add(route);

                    var interfaceTypes = type.GetInterfaces();
                    if (!interfaceTypes.IsNullOrEmpty())
                    {
                        foreach (var interfaceType in interfaceTypes)
                        {
                            GetRoutes().TryGetValue(interfaceType, out route);
                            if (route != null) routes.Add(route);
                        }
                    }
                }
            }

            return routes;
        }


        /// <summary>
        /// Returns true if route is the Wildcard, equals pattern, or is a Regular Expression that matches pattern
        /// </summary>
        public static bool IsMatch(string pattern, string route)
        {
            if (route == Wildcard) return true;
            if (pattern == route) return true;

            try
            {
                var regex = new Regex(route);
                var match = regex.Match(pattern);

                if (match != null && match.Success) return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Returns true if route is the Wildcard, equals one of the type's Routes, or is a Regular Expression that matches one of them
        /// </summary>
        public static bool IsMatch(Type type, string route)
        {
            var types = new List<Type> { type };
            var typeRoutes = Get(types);
            if (!typeRoutes.IsNullOrEmpty())
            {
                foreach (var typeRoute in typeRoutes)
                {
                    if (route == Wildcard) return true;
                    if (typeRoute == route) return true;

                    try
                    {
                        var regex = new Regex(route);
                        var match = regex.Match(typeRoute);

                        if (match != null && match.Success) return true;
                    }
                    catch { }
                }
            }

            return false;
        }
    }
}
