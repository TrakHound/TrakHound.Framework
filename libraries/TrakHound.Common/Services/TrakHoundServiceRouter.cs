// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Services.Blobs;
using TrakHound.Services.KeyValues;
using TrakHound.Services.MessageQueues;
using TrakHound.Services.Messages;
using TrakHound.Services.Tables;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Services
{
    /// <summary>
    /// Routes Service requests (Blobs, KeyValues, Messages, MessageQueues, Tables, TimeSeries) to the
    /// Services and Routers configured as Targets in a <see cref="TrakHoundServiceRouterConfiguration"/>.
    /// Targets are tried in order until one succeeds.
    /// </summary>
    public class TrakHoundServiceRouter
    {
        /// <summary>
        /// The Name of the default Router
        /// </summary>
        public const string Default = "default";

        private readonly ConcurrentDictionary<string, bool> _routeCache = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, IRouteTarget[]> _routeTargets = new ConcurrentDictionary<string, IRouteTarget[]>(); // Route => Matching Targets

        private readonly string _id;
        private readonly TrakHoundServiceRouterConfiguration _configuration;
        private readonly ITrakHoundLogger _logger;
        private readonly IEnumerable<IRouteTarget> _targets;
        private readonly TrakHoundBlobRouter _blobs;
        private readonly TrakHoundKeyValueRouter _keyValues;
        private readonly TrakHoundMessageRouter _messages;
        private readonly TrakHoundMessageQueueRouter _messageQueues;
        private readonly TrakHoundTableRouter _tables;
        private readonly TrakHoundTimeSeriesRouter _timeSeries;

        private volatile bool _initialized;


        /// <summary>
        /// The Unique Identifier of the Router (from the Configuration)
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Whether <see cref="Initialize()"/> has been called and the Router is ready to handle requests
        /// </summary>
        public bool Initialized => _initialized;

        /// <summary>
        /// The Configuration the Router was created from
        /// </summary>
        public TrakHoundServiceRouterConfiguration Configuration => _configuration;

        /// <summary>
        /// The Client associated with the Router
        /// </summary>
        public ITrakHoundClient Client { get; set; }

        /// <summary>
        /// Receives the result of each request this Router sends to a Service (optional)
        /// </summary>
        public ITrakHoundServiceMetricsRecorder MetricsRecorder { get; set; }

        /// <summary>
        /// Routes requests to Blob Services
        /// </summary>
        public TrakHoundBlobRouter Blobs => _blobs;

        /// <summary>
        /// Routes requests to KeyValue Services
        /// </summary>
        public TrakHoundKeyValueRouter KeyValues => _keyValues;

        /// <summary>
        /// Routes requests to Message Services
        /// </summary>
        public TrakHoundMessageRouter Messages => _messages;

        /// <summary>
        /// Routes requests to MessageQueue Services
        /// </summary>
        public TrakHoundMessageQueueRouter MessageQueues => _messageQueues;

        /// <summary>
        /// Routes requests to Table Services
        /// </summary>
        public TrakHoundTableRouter Tables => _tables;

        /// <summary>
        /// Routes requests to Time Series Services
        /// </summary>
        public TrakHoundTimeSeriesRouter TimeSeries => _timeSeries;

        /// <summary>
        /// The Logger used for Routing (named "routers/{Id}")
        /// </summary>
        public ITrakHoundLogger Logger => _logger;


        /// <summary>
        /// Creates a new Router from the specified Configuration. Call <see cref="MapTargets"/> and then
        /// <see cref="Initialize()"/> before routing requests.
        /// </summary>
        /// <param name="configuration">The Router Configuration containing the Routes and Targets</param>
        /// <param name="logProvider">The Log Provider used to create the Router's Logger (a standalone Logger is used if null)</param>
        /// <param name="serviceProvider">Not currently used</param>
        public TrakHoundServiceRouter(
            TrakHoundServiceRouterConfiguration configuration,
            ITrakHoundLogProvider logProvider,
            ITrakHoundServiceProvider serviceProvider
            )
        {
            _blobs = new TrakHoundBlobRouter(this);
            _keyValues = new TrakHoundKeyValueRouter(this);
            _messages = new TrakHoundMessageRouter(this);
            _messageQueues = new TrakHoundMessageQueueRouter(this);
            _tables = new TrakHoundTableRouter(this);
            _timeSeries = new TrakHoundTimeSeriesRouter(this);

            _configuration = configuration;
            _id = configuration?.Id;

            // Always provide a Logger since RouteTarget logs every request through it
            var loggerName = $"routers/{_id}";
            _logger = logProvider?.GetLoggerByName(loggerName) ?? new TrakHoundLogger(loggerName);

            // Targets are created once and never replaced, so they can be read without locking
            _targets = InitializeTargets(configuration);
        }


        /// <summary>
        /// Marks the Router as ready to handle requests. Call after <see cref="MapTargets"/>.
        /// </summary>
        public void Initialize()
        {
            _initialized = true;
        }

        /// <summary>
        /// Marks the Router as ready to handle requests. The parameters are ignored; use <see cref="MapTargets"/> to assign Targets.
        /// </summary>
        [Obsolete("The parameters are ignored. Call MapTargets(routers, services) and then Initialize().")]
        public void Initialize(IEnumerable<TrakHoundServiceRouter> routers, IEnumerable<ITrakHoundService> drivers) => Initialize();


        #region "Mapping"

        /// <summary>
        /// Assigns the matching Services and Routers to each of this Router's Targets.
        /// Can be called again to remap when the available Services or Routers change.
        /// </summary>
        /// <param name="routers">All available Routers (used for Router Targets)</param>
        /// <param name="services">All available Services (used for Service Targets)</param>
        public void MapTargets(IEnumerable<TrakHoundServiceRouter> routers, IEnumerable<ITrakHoundService> services)
        {
            try
            {
                MapTargets(_targets, services);
                MapTargets(_targets, routers);
            }
            catch { } // Ignore Collection Modified and Object Disposed Exceptions
        }

        private void MapTargets(IEnumerable<IRouteTarget> targets, IEnumerable<ITrakHoundService> services)
        {
            if (!targets.IsNullOrEmpty())
            {
                foreach (var target in targets)
                {
                    MapTargets(target, services);
                }
            }
        }

        private void MapTargets(IRouteTarget target, IEnumerable<ITrakHoundService> services)
        {
            if (target != null)
            {
                if (target is IRouteServiceTarget serviceTarget)
                {
                    // Assign Services
                    serviceTarget.Services = GetTargetServices(serviceTarget, services);
                }
            }
        }


        private void MapTargets(IEnumerable<IRouteTarget> targets, IEnumerable<TrakHoundServiceRouter> routers)
        {
            if (!targets.IsNullOrEmpty())
            {
                foreach (var target in targets)
                {
                    MapTargets(target, routers);
                }
            }
        }

        private void MapTargets(IRouteTarget target, IEnumerable<TrakHoundServiceRouter> routers)
        {
            if (target != null && !routers.IsNullOrEmpty())
            {
                // Map Router
                if (target is IRouteRouterTarget routerTarget)
                {
                    // Assign Router
                    if (target.Id == _id)
                    {
                        // A Router targeting itself would recurse until the process crashes
                        routerTarget.Router = null;
                        _logger.LogError($"Router ({_id}) : Route Target references itself and will be ignored");
                    }
                    else
                    {
                        routerTarget.Router = routers.FirstOrDefault(o => o != null && o.Id == target.Id);
                    }
                }
            }
        }

        #endregion

        #region "Targets"

        /// <summary>
        /// Gets the Targets whose Route Patterns match the specified Route (ex. "Blobs"), in the order they are configured
        /// (by Route, then by Target). They are tried in this order (see <see cref="TrakHoundServiceFallback"/>), and each Target is only returned once.
        /// </summary>
        internal IEnumerable<IRouteTarget> GetTargets(string route)
        {
            if (string.IsNullOrEmpty(route)) return Array.Empty<IRouteTarget>();

            // The Targets and their Patterns never change, so the matches for each Route are only built once (this runs on every request)
            if (_routeTargets.TryGetValue(route, out var targets)) return targets;
            return _routeTargets.GetOrAdd(route, BuildTargets);
        }

        private IRouteTarget[] BuildTargets(string route)
        {
            var targets = new List<IRouteTarget>();

            if (!string.IsNullOrEmpty(route) && !_targets.IsNullOrEmpty())
            {
                var added = new HashSet<IRouteTarget>();

                foreach (var target in _targets)
                {
                    if (IsRouteMatch(target, route) && added.Add(target)) targets.Add(target);
                }
            }

            return targets.ToArray();
        }

        /// <summary>
        /// Gets the Targets whose Route Patterns match any of the specified Routes. Each Target is only returned once.
        /// </summary>
        internal IEnumerable<IRouteTarget> GetTargets(IEnumerable<string> routes)
        {
            var targets = new List<IRouteTarget>();

            if (!routes.IsNullOrEmpty())
            {
                // Each Target is only added once (even if it matches multiple Routes) so that a failed Target is not retried
                var added = new HashSet<IRouteTarget>();

                foreach (var route in routes)
                {
                    foreach (var target in GetTargets(route))
                    {
                        if (added.Add(target)) targets.Add(target);
                    }
                }
            }

            return targets;
        }

        private bool IsRouteMatch(IRouteTarget target, string route)
        {
            return TrakHoundServiceRouteResolver.IsRouteMatch(target?.RouteConfiguration?.Patterns, route, MatchRoute);
        }

        /// <summary>
        /// Creates the Service and Router Targets defined in the Router Configuration's Routes
        /// </summary>
        internal static IEnumerable<IRouteTarget> InitializeTargets(TrakHoundServiceRouterConfiguration routerConfiguration)
        {
            var targets = new List<IRouteTarget>();

            if (routerConfiguration != null && !routerConfiguration.Routes.IsNullOrEmpty())
            {
                foreach (var routeConfiguration in routerConfiguration.Routes)
                {
                    if (!routeConfiguration.Targets.IsNullOrEmpty())
                    {
                        foreach (var targetConfiguration in routeConfiguration.Targets)
                        {
                            if (!string.IsNullOrEmpty(targetConfiguration.Type))
                            {
                                if (targetConfiguration.Type.ToLower() == TrakHoundServiceRouteTargetType.Service.ToString().ToLower())
                                {
                                    // Add Driver Target
                                    targets.Add(new RouteServiceTarget(routerConfiguration, routeConfiguration, targetConfiguration));
                                }
                                else if (targetConfiguration.Type.ToLower() == TrakHoundServiceRouteTargetType.Router.ToString().ToLower())
                                {
                                    // Add Router Target
                                    targets.Add(new RouteRouterTarget(routerConfiguration, routeConfiguration, targetConfiguration));
                                }
                            }
                        }
                    }
                }
            }

            return targets;
        }

        /// <summary>
        /// Gets the Services for a Service Target. A Service is included if it matches any allow Pattern, does not match
        /// any deny Pattern (prefixed with "!"), and its Configuration ID matches the Target ID (or the Target ID is "*").
        /// A Target with only deny Patterns matches no Services.
        /// </summary>
        internal static IEnumerable<ITrakHoundService> GetTargetServices(IRouteServiceTarget target, IEnumerable<ITrakHoundService> services)
        {
            if (target?.RouteConfiguration == null) return new List<ITrakHoundService>();

            return TrakHoundServiceRouteResolver.SelectServices(
                target.RouteConfiguration.Patterns,
                target.Id,
                services,
                o => o.Id,
                o => o.Configuration?.Id,
                (o, pattern) => o.IsRouteMatch(pattern));
        }

        private bool MatchRoute(string routePattern, string route)
        {
            if (string.IsNullOrEmpty(routePattern) || string.IsNullOrEmpty(route)) return false;
            if (routePattern == TrakHoundServiceRoutes.Wildcard) return true;
            if (routePattern == route) return true;

            var cacheKey = $"{routePattern}:{route}";
            if (_routeCache.TryGetValue(cacheKey, out var matched)) return matched;

            try
            {
                matched = TrakHoundServiceRouteResolver.IsPatternMatch(routePattern, route);
            }
            catch (Exception ex)
            {
                // Cache invalid Patterns as not matched so the Regex isn't rebuilt (and doesn't throw) on every request
                matched = false;
                _logger.LogError($"Router ({_id}) : Invalid Route Pattern ({routePattern}) : {ex.Message}");
            }

            _routeCache[cacheKey] = matched;
            return matched;
        }

        #endregion

    }
}
