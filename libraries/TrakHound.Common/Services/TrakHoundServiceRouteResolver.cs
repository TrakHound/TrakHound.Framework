// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrakHound.Services
{
    /// <summary>
    /// The rules a <see cref="TrakHoundServiceRouter"/> uses to pick the Targets and Services for a Request, and <see cref="Resolve"/>,
    /// which applies them to Router Configurations (ex. to show where a Request would go before the Routers are saved).
    /// The Router calls the same methods, so the two can't disagree.
    /// </summary>
    public static class TrakHoundServiceRouteResolver
    {
        /// <summary>
        /// Returns true if the Pattern is the Wildcard, equals the Route, or is a Regular Expression that matches it (ex. "KeyValues").
        /// Throws an ArgumentException if the Pattern isn't a valid Regular Expression
        /// </summary>
        public static bool IsPatternMatch(string pattern, string route)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(route)) return false;
            if (pattern == TrakHoundServiceRoutes.Wildcard) return true;
            if (pattern == route) return true;

            return Regex.IsMatch(route, pattern);
        }

        /// <summary>
        /// Returns true if any of a Route's Patterns matches the Route. Invalid Patterns don't match
        /// </summary>
        /// <param name="isPatternMatch">Matches one Pattern (defaults to <see cref="IsPatternMatch"/>; the Router passes one that caches its results)</param>
        public static bool IsRouteMatch(IEnumerable<string> patterns, string route, Func<string, string, bool> isPatternMatch = null)
        {
            if (patterns.IsNullOrEmpty()) return false;

            isPatternMatch ??= TryPatternMatch;
            foreach (var pattern in patterns)
            {
                if (isPatternMatch(pattern, route)) return true;
            }

            return false;
        }

        /// <summary>
        /// Selects the Services a Service Target sends Requests to, in order. A Service is included if it matches any allow Pattern,
        /// does not match any deny Pattern (prefixed with "!"), and its Configuration ID is the Target's Source (or the Source is "*").
        /// A Target with only deny Patterns selects no Services.
        /// </summary>
        /// <param name="patterns">The Patterns of the Target's Route</param>
        /// <param name="source">The Target's Source (a Service Configuration ID, or "*")</param>
        /// <param name="services">The available Services</param>
        /// <param name="getKey">Identifies a Service (each is only selected once)</param>
        /// <param name="getConfigurationId">The Configuration ID of a Service (compared to the Source)</param>
        /// <param name="isPatternMatch">Whether a Service handles a Route matching the Pattern</param>
        /// <param name="isDenyMatch">Whether a deny Pattern (without its "!") excludes a Service (defaults to isPatternMatch)</param>
        public static List<TService> SelectServices<TService>(
            IEnumerable<string> patterns,
            string source,
            IEnumerable<TService> services,
            Func<TService, string> getKey,
            Func<TService, string> getConfigurationId,
            Func<TService, string, bool> isPatternMatch,
            Func<TService, string, bool> isDenyMatch = null)
        {
            isDenyMatch ??= isPatternMatch;

            var selected = new List<TService>();
            if (patterns.IsNullOrEmpty() || services.IsNullOrEmpty()) return selected;

            var patternList = patterns.Where(o => !string.IsNullOrEmpty(o)).ToList();
            var allowPatterns = patternList.Where(o => !o.StartsWith('!')).ToList();
            var denyPatterns = patternList.Where(o => o.StartsWith('!')).Select(o => o.Substring(1)).ToList();
            var availableServices = services.Where(o => o != null).ToList();

            // Services matching a Deny Pattern
            var deniedKeys = new HashSet<string>();
            foreach (var pattern in denyPatterns)
            {
                foreach (var service in availableServices.Where(o => isDenyMatch(o, pattern)))
                {
                    deniedKeys.Add(getKey(service));
                }
            }

            // Services matching an Allow Pattern (in Pattern order) that aren't Denied and match the Source
            var addedKeys = new HashSet<string>();
            foreach (var pattern in allowPatterns)
            {
                foreach (var service in availableServices.Where(o => isPatternMatch(o, pattern)))
                {
                    var isSourceMatch = source == TrakHoundServiceRoutes.Wildcard || getConfigurationId(service) == source;
                    var key = getKey(service);

                    if (isSourceMatch && !deniedKeys.Contains(key) && addedKeys.Add(key))
                    {
                        selected.Add(service);
                    }
                }
            }

            return selected;
        }

        /// <summary>
        /// The Router a Request naming the Router Key is sent to: a Router by ID, then by Name (ignoring case).
        /// With no Key, the Router named "default" (or the first Router). Null if there isn't one
        /// (a Key can also be a Service's Configuration ID, which sends the Request straight to that Service)
        /// </summary>
        public static TrakHoundServiceRouterConfiguration FindRouter(IEnumerable<TrakHoundServiceRouterConfiguration> routers, string routerKey)
        {
            var routerList = routers?.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList();
            if (routerList.IsNullOrEmpty()) return null;

            if (string.IsNullOrEmpty(routerKey))
            {
                return routerList.FirstOrDefault(o => o.Name == TrakHoundServiceRouter.Default) ?? routerList.First();
            }

            return routerList.FirstOrDefault(o => o.Id == routerKey)
                ?? routerList.FirstOrDefault(o => string.Equals(o.Name, routerKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Works out where a Request for the Route (ex. "KeyValues") sent to the Router would go: its Targets in the order they are tried,
        /// the Services each Service Target sends it to, and the path through each Router Target's Router
        /// </summary>
        /// <param name="routers">Every Router Configuration</param>
        /// <param name="routerId">The ID of the Router the Request is sent to</param>
        /// <param name="route">The kind of Request (ex. "KeyValues")</param>
        /// <param name="services">The configured Services (and the Routes they handle, if known)</param>
        public static TrakHoundServiceRoutePath Resolve(
            IEnumerable<TrakHoundServiceRouterConfiguration> routers,
            string routerId,
            string route,
            IEnumerable<TrakHoundServiceRouteService> services)
        {
            var routerList = routers?.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList() ?? new List<TrakHoundServiceRouterConfiguration>();
            var serviceList = services?.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList() ?? new List<TrakHoundServiceRouteService>();

            return Resolve(routerList, routerList.FirstOrDefault(o => o.Id == routerId), route, serviceList, new List<string>());
        }

        private static TrakHoundServiceRoutePath Resolve(
            List<TrakHoundServiceRouterConfiguration> routers,
            TrakHoundServiceRouterConfiguration router,
            string route,
            List<TrakHoundServiceRouteService> services,
            List<string> chain)
        {
            var path = new TrakHoundServiceRoutePath { RouterId = router?.Id, RouterName = router?.Name, Route = route };
            if (router == null) return path;

            // The Routers the Request has passed through (a Router Target back to one of them would loop)
            chain = new List<string>(chain) { router.Id };

            foreach (var routeConfiguration in router.Routes ?? Enumerable.Empty<TrakHoundServiceRouteConfiguration>())
            {
                if (routeConfiguration == null || !IsRouteMatch(routeConfiguration.Patterns, route)) continue;

                foreach (var targetConfiguration in routeConfiguration.Targets ?? Enumerable.Empty<TrakHoundServiceTargetConfiguration>())
                {
                    if (targetConfiguration == null || string.IsNullOrEmpty(targetConfiguration.Type)) continue;

                    var target = new TrakHoundServiceRoutePathTarget
                    {
                        RouteId = routeConfiguration.Id,
                        TargetId = targetConfiguration.Id,
                        Type = targetConfiguration.Type,
                        Source = targetConfiguration.Source,
                        SourceName = targetConfiguration.Source,
                        FallbackOn = TrakHoundServiceFallback.GetFallbackOn(targetConfiguration).OrderBy(o => o).ToList()
                    };

                    if (string.Equals(targetConfiguration.Type, nameof(TrakHoundServiceRouteTargetType.Service), StringComparison.OrdinalIgnoreCase))
                    {
                        var source = services.FirstOrDefault(o => o.Id == targetConfiguration.Source);
                        if (!string.IsNullOrEmpty(source?.Name)) target.SourceName = source.Name;

                        target.Services = SelectServices(routeConfiguration.Patterns, targetConfiguration.Source, services, o => o.Id, o => o.Id, IsServicePatternMatch, IsServiceDenyMatch)
                            .Where(o => o.Routes == null || o.Routes.Contains(route))
                            .ToList();

                        if (target.Services.Count == 0)
                        {
                            target.Problem = targetConfiguration.Source != TrakHoundServiceRoutes.Wildcard && source == null
                                ? $"The Service ({targetConfiguration.Source}) isn't configured"
                                : $"No Service handles {route}, so it is skipped";
                        }
                    }
                    else if (string.Equals(targetConfiguration.Type, nameof(TrakHoundServiceRouteTargetType.Router), StringComparison.OrdinalIgnoreCase))
                    {
                        var targetRouter = routers.FirstOrDefault(o => o.Id == targetConfiguration.Source);
                        if (!string.IsNullOrEmpty(targetRouter?.Name)) target.SourceName = targetRouter.Name;

                        if (targetRouter == null)
                        {
                            target.Problem = $"The Router ({targetConfiguration.Source}) doesn't exist";
                        }
                        else if (chain.Contains(targetRouter.Id))
                        {
                            target.Problem = $"Requests would loop back to the \"{targetRouter.Name}\" Router, so it is skipped";
                        }
                        else
                        {
                            target.Router = Resolve(routers, targetRouter, route, services, chain);
                        }
                    }
                    else
                    {
                        target.Problem = $"The Target Type ({targetConfiguration.Type}) isn't supported";
                    }

                    path.Targets.Add(target);
                }
            }

            return path;
        }

        // A Service whose Routes aren't known might handle any of them, so it is kept (and shown as unknown)
        public static bool IsServicePatternMatch(TrakHoundServiceRouteService service, string pattern)
        {
            return service.Routes == null || service.Routes.Any(o => TryPatternMatch(pattern, o));
        }

        // ...and only excluded by a deny Pattern when its Routes are known
        public static bool IsServiceDenyMatch(TrakHoundServiceRouteService service, string pattern)
        {
            return service.Routes != null && service.Routes.Any(o => TryPatternMatch(pattern, o));
        }

        private static bool TryPatternMatch(string pattern, string route)
        {
            try
            {
                return IsPatternMatch(pattern, route);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
