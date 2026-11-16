// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Logging;

namespace TrakHound.Services
{
    /// <summary>
    /// Creates and provides access to Service Routers, which route requests to a combination of Services.
    /// Includes the configured Routers and a Direct Router for each individual Service.
    /// </summary>
    public class TrakHoundServiceRouterProvider : IDisposable
    {
        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly ITrakHoundLogger _logger;
        private readonly ITrakHoundServiceProvider _serviceProvider;
        private readonly TrakHoundServiceMetricsCollector _metrics = new TrakHoundServiceMetricsCollector();

        // Replaced (not modified) on each Load, so readers always see a complete and initialized set
        private volatile RouterSet _routerSet = RouterSet.Empty;

        private readonly DelayEvent _loadDelay = new DelayEvent(1000);
        private readonly object _loadLock = new object();
        private bool _disposed;


        /// <summary>
        /// List of Routers used to access a combination of Services
        /// </summary>
        public IEnumerable<TrakHoundServiceRouter> Routers => _routerSet.Routers.Values.ToList();

        /// <summary>
        /// Request Metrics for each Service (recorded by every Router)
        /// </summary>
        public TrakHoundServiceMetricsCollector Metrics => _metrics;

        /// <summary>
        /// Record Service Metrics (set before the Routers are loaded; Routers are not given a Recorder when disabled)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;

        public event EventHandler<TrakHoundServiceRouter> RouterAdded;

        public event EventHandler<string> RouterRemoved;


        public TrakHoundServiceRouterProvider(
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundLogProvider logProvider,
            ITrakHoundServiceProvider serviceProvider
            )
        {
            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

            _logProvider = logProvider;
            _logger = logProvider?.GetLoggerByName("services");

            _serviceProvider = serviceProvider;
            _serviceProvider.ServiceAdded += ServiceAdded;
            _serviceProvider.ServiceRemoved += ServiceRemoved;

            _loadDelay.Elapsed += LoadDelayElapsed;
        }


        public void Dispose()
        {
            lock (_loadLock)
            {
                if (_disposed) return;
                _disposed = true;

                _loadDelay.Elapsed -= LoadDelayElapsed;
                _loadDelay.Dispose();

                _configurationProfile.ConfigurationAdded -= ConfigurationUpdated;
                _configurationProfile.ConfigurationRemoved -= ConfigurationUpdated;

                _serviceProvider.ServiceAdded -= ServiceAdded;
                _serviceProvider.ServiceRemoved -= ServiceRemoved;

                _routerSet = RouterSet.Empty;
            }
        }


        private void ConfigurationUpdated(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration != null && configuration.Category == TrakHoundServiceRouterConfiguration.ConfigurationCategory)
            {
                _loadDelay.Set();
            }
        }

        private void ServiceAdded(object sender, ITrakHoundService service)
        {
            _loadDelay.Set();
        }

        private void ServiceRemoved(object sender, string serviceId)
        {
            _loadDelay.Set();
        }

        private void LoadDelayElapsed(object sender, EventArgs args)
        {
            // Exceptions in Timer callbacks are swallowed, so log them here
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Service Router Load Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        /// <summary>
        /// Create, Map, and Initialize all Routers and then replace the current Routers with them
        /// </summary>
        public void Load()
        {
            lock (_loadLock)
            {
                if (_disposed || _configurationProfile == null || _serviceProvider == null) return;

                var services = _serviceProvider.Services?.ToList() ?? new List<ITrakHoundService>();

                var routers = CreateRouters();
                var directRouters = CreateDirectRouters(services);

                // Map and Initialize before the Routers are used
                InitializeRouters(routers, services);
                InitializeRouters(directRouters, services);

                var previousRouters = _routerSet.Routers;
                _routerSet = new RouterSet(routers, directRouters);

                // Only report Routers that were added or removed (not every Router on every Load)
                foreach (var router in routers)
                {
                    if (!previousRouters.ContainsKey(router.Key)) RouterAdded?.Invoke(this, router.Value);
                }

                foreach (var previousRouterId in previousRouters.Keys)
                {
                    if (!routers.ContainsKey(previousRouterId)) RouterRemoved?.Invoke(this, previousRouterId);
                }
            }
        }


        public IEnumerable<TrakHoundServiceRouterInformation> GetInformation()
        {
            var informations = new List<TrakHoundServiceRouterInformation>();

            foreach (var router in _routerSet.Routers.Values)
            {
                var information = TrakHoundServiceRouterInformationFactory.Create(router);
                if (information != null) informations.Add(information);
            }

            return informations;
        }

        public TrakHoundServiceRouterInformation GetInformation(string routerId)
        {
            if (!string.IsNullOrEmpty(routerId))
            {
                var router = _routerSet.Routers.GetValueOrDefault(routerId);
                if (router != null)
                {
                    return TrakHoundServiceRouterInformationFactory.Create(router);
                }
            }

            return null;
        }


        public TrakHoundServiceRouter AddRouter(TrakHoundServiceRouterConfiguration configuration)
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Id) && _configurationProfile != null)
            {
                var exists = _routerSet.Routers.ContainsKey(configuration.Id);

                // Save the Configuration (also updates an existing Router's Configuration)
                _configurationProfile.Add(configuration);

                // Reload now (instead of waiting for the delayed Load) so the new Router can be returned
                Load();

                // Only the newly added Router is returned (as before)
                return !exists ? _routerSet.Routers.GetValueOrDefault(configuration.Id) : null;
            }

            return null;
        }

        public void RemoveRouter(string routerId)
        {
            if (!string.IsNullOrEmpty(routerId) && _configurationProfile != null)
            {
                _configurationProfile.Remove(TrakHoundServiceRouterConfiguration.ConfigurationCategory, routerId);

                // RouterRemoved is raised by Load
                Load();
            }
        }


        private Dictionary<string, TrakHoundServiceRouter> CreateRouters()
        {
            var routers = new Dictionary<string, TrakHoundServiceRouter>();

            // Configured Routers
            var configurations = _configurationProfile.Get<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    if (!string.IsNullOrEmpty(configuration.Id) && !routers.ContainsKey(configuration.Id))
                    {
                        routers.Add(configuration.Id, new TrakHoundServiceRouter(configuration, _logProvider, _serviceProvider) { MetricsRecorder = MetricsEnabled ? _metrics : null });
                    }
                }
            }

            return routers;
        }

        private Dictionary<string, TrakHoundServiceRouter> CreateDirectRouters(IEnumerable<ITrakHoundService> services)
        {
            var routers = new Dictionary<string, TrakHoundServiceRouter>();

            // Create Individual Direct Routers for each Service
            foreach (var service in services)
            {
                if (service.Configuration != null && !string.IsNullOrEmpty(service.Configuration.Id) && !routers.ContainsKey(service.Configuration.Id))
                {
                    var routerConfiguration = new TrakHoundServiceRouterConfiguration();
                    routerConfiguration.Id = service.Configuration.Id;
                    routerConfiguration.Name = service.Configuration.Name;

                    var serviceTarget = new TrakHoundServiceTargetConfiguration();
                    serviceTarget.Source = service.Configuration.Id;
                    serviceTarget.Type = "Service";

                    var route = new TrakHoundServiceRouteConfiguration();
                    route.Patterns = new string[] { "*" };
                    route.AddTarget(serviceTarget);
                    routerConfiguration.AddRoute(route);

                    routers.Add(routerConfiguration.Id, new TrakHoundServiceRouter(routerConfiguration, _logProvider, _serviceProvider) { MetricsRecorder = MetricsEnabled ? _metrics : null });
                }
            }

            return routers;
        }

        private static void InitializeRouters(Dictionary<string, TrakHoundServiceRouter> routers, IEnumerable<ITrakHoundService> services)
        {
            var routerList = routers.Values.ToList();

            foreach (var router in routerList) router.MapTargets(routerList, services);
            foreach (var router in routerList) router.Initialize();
        }


        public TrakHoundServiceRouter GetRouter()
        {
            var router = _routerSet.DefaultRouter;
            if (router != null && router.Initialized) return router;

            return null;
        }

        public TrakHoundServiceRouter GetRouter(string routerKey)
        {
            if (string.IsNullOrEmpty(routerKey)) return GetRouter();

            var routerSet = _routerSet;

            // Configured Router (by ID), then Direct Router for a Service (by ID), then Configured Router (by Name)
            var router = routerSet.Routers.GetValueOrDefault(routerKey);
            if (router == null) router = routerSet.DirectRouters.GetValueOrDefault(routerKey);
            if (router == null) router = routerSet.RoutersByName.GetValueOrDefault(routerKey);

            if (router != null && router.Initialized) return router;

            return null;
        }


        /// <summary>
        /// Gets the Direct Router for the Service with the specified Configuration ID (null if there isn't one)
        /// </summary>
        public TrakHoundServiceRouter GetDirectRouter(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId)) return null;

            var router = _routerSet.DirectRouters.GetValueOrDefault(serviceId);
            return router != null && router.Initialized ? router : null;
        }

        /// <summary>
        /// Whether the Router is one of the current Routers (false once a Load has replaced it). Lets a caller that keeps
        /// a Router (ex. for a Publish Stream) resolve it again only when the Routers are reloaded.
        /// </summary>
        public bool IsCurrent(TrakHoundServiceRouter router)
        {
            if (router == null || string.IsNullOrEmpty(router.Id)) return false;

            var routerSet = _routerSet;
            return routerSet.Routers.GetValueOrDefault(router.Id) == router || routerSet.DirectRouters.GetValueOrDefault(router.Id) == router;
        }


        // The Routers of one Load, with the lookups that would otherwise scan them on every request
        private class RouterSet
        {
            public static readonly RouterSet Empty = new RouterSet(new Dictionary<string, TrakHoundServiceRouter>(), new Dictionary<string, TrakHoundServiceRouter>());

            public readonly Dictionary<string, TrakHoundServiceRouter> Routers; // Router ID => Router
            public readonly Dictionary<string, TrakHoundServiceRouter> DirectRouters; // Service ID => Direct Router
            public readonly Dictionary<string, TrakHoundServiceRouter> RoutersByName; // Router Name (case insensitive) => first Router with that Name
            public readonly TrakHoundServiceRouter DefaultRouter;

            public RouterSet(Dictionary<string, TrakHoundServiceRouter> routers, Dictionary<string, TrakHoundServiceRouter> directRouters)
            {
                Routers = routers;
                DirectRouters = directRouters;
                RoutersByName = new Dictionary<string, TrakHoundServiceRouter>(StringComparer.OrdinalIgnoreCase);

                foreach (var router in routers.Values)
                {
                    if (router != null && !string.IsNullOrEmpty(router.Id) && !string.IsNullOrEmpty(router.Configuration?.Name))
                    {
                        RoutersByName.TryAdd(router.Configuration.Name, router);
                    }
                }

                // The Router named "default", or the first Router
                var defaultConfiguration = TrakHoundServiceRouteResolver.FindRouter(routers.Values.Select(o => o.Configuration), null);
                DefaultRouter = routers.Values.FirstOrDefault(o => o.Configuration == defaultConfiguration);
            }
        }
    }
}
