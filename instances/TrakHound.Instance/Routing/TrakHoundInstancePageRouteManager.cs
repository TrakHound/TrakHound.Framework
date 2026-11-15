// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace TrakHound.Blazor.Routing
{
    public class TrakHoundInstancePageRouteManager : ITrakHoundPageRouteManager, IDisposable
    {
        private static readonly RouteComparer _routeComparer = new RouteComparer();

        private readonly object _lock = new object();

        // Routes discovered from the Entry Assembly (only scanned once)
        private Route[] _instanceRoutes;

        // Ordered for Matching. Replaced (not modified) by Initialise so that Match (ex. from concurrent Circuits) never sees an empty or partially built set
        private volatile Route[] _routes = Array.Empty<Route>();
        private volatile bool _initialised;

        public IEnumerable<Route> Routes => _routes;


        public void Dispose()
        {
            Clear();
        }


        public void Clear()
        {
            lock (_lock)
            {
                _instanceRoutes = null;
                _routes = Array.Empty<Route>();
                _initialised = false;
            }
        }

        public void ClearPages()
        {
            _routes = Array.Empty<Route>();
            _initialised = false;
        }


        public void Initialise()
        {
            lock (_lock)
            {
                _instanceRoutes ??= LoadAssemblyRoutes();

                _routes = _instanceRoutes.OrderBy(o => o.Template, _routeComparer).ToArray();
                _initialised = true;
            }
        }

        // Routers call this for every page (a Router is created for each Request), so the Routes are only sorted when they haven't been
        public void EnsureInitialised()
        {
            if (!_initialised) Initialise();
        }

        private static Route[] LoadAssemblyRoutes()
        {
            var routes = new List<Route>();

            // Not the App Host (its catch-all would match every route)
            var pageComponentTypes = Assembly.GetEntryAssembly()?.ExportedTypes.Where(t => t.IsSubclassOf(typeof(ComponentBase)) && t != typeof(TrakHound.Instance.Pages.AppHost));
            if (pageComponentTypes != null)
            {
                foreach (var pageComponentType in pageComponentTypes)
                {
                    foreach (var routeAttribute in pageComponentType.GetCustomAttributes<RouteAttribute>())
                    {
                        if (!string.IsNullOrEmpty(routeAttribute.Template))
                        {
                            routes.Add(new Route
                            {
                                Template = routeAttribute.Template,
                                Handler = pageComponentType
                            });
                        }
                    }
                }
            }

            return routes.ToArray();
        }

        public MatchResult Match(string url)
        {
            // Read the reference once so a concurrent Initialise can't swap it mid-loop
            var routes = _routes;
            foreach (var route in routes)
            {
                var matchResult = route.Match(url);
                if (matchResult.IsMatch)
                {
                    return matchResult;
                }
            }

            return MatchResult.NoMatch();
        }


        /// <summary>
        /// Literal Segments before constrained, plain, optional and catch-all Parameters (see TrakHoundRouteTemplate.Comparer)
        /// </summary>
        public class RouteComparer : TrakHoundRouteTemplate.Comparer { }
    }
}
