// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Reflection;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Instance.Http;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Volumes;

namespace TrakHound.Blazor.Routing
{
    public class TrakHoundAppPageRouteManager<T> : TrakHoundAppPageRouteManager
    {
        public TrakHoundAppPageRouteManager(
            string baseUrl,
            ITrakHoundInstance instance,
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider, 
            string packageCategory,
            TrakHoundModuleContext context,
            ITrakHoundClientProvider clientProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundLogProvider logProvider
            )
            : base(baseUrl)
        {
            _moduleManager = moduleProvider.Get<T>(packageCategory, context);

            _instance = instance;
            _configurationProfile = configurationProfile;
            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _logProvider = logProvider;
        }
    }


    public abstract class TrakHoundAppPageRouteManager : ITrakHoundPageRouteManager, IDisposable
    {
        private const string _packagesDirectory = "_packages";
        private static readonly RouteComparer _routeComparer = new RouteComparer();


        private readonly string _baseUrl;
        private readonly ListDictionary<string, Route> _instanceRoutes = new ListDictionary<string, Route>();
        private volatile ListDictionary<string, Route> _routes = new ListDictionary<string, Route>(); // Replaced (not modified) by Initialise so readers never see a partial set
        private volatile Route[] _orderedRoutes = Array.Empty<Route>(); // The Routes in the order they are matched (sorted once instead of for every Match)
        private volatile bool _initialised;
        private readonly object _initialiseLock = new object();
        private readonly Dictionary<string, ITrakHoundAppConfiguration> _appConfigurations = new Dictionary<string, ITrakHoundAppConfiguration>();
        private readonly DelayEvent _updateDelay = new DelayEvent(2000);
        private readonly object _lock = new object();

        private bool _loaded;

        protected ITrakHoundInstance _instance;
        protected ITrakHoundConfigurationProfile _configurationProfile;
        protected ITrakHoundModuleManager _moduleManager;
        protected ITrakHoundClientProvider _clientProvider;
        protected ITrakHoundVolumeProvider _volumeProvider;
        protected ITrakHoundLogProvider _logProvider;


        public IEnumerable<Route> Routes => _routes.Values;


        public event EventHandler<string> AppUpdated; // AppId as argument


        public TrakHoundAppPageRouteManager(string baseUrl)
        {
            _baseUrl = baseUrl;
            _updateDelay.Elapsed += UpdateDelayElapsed;
        }

        public void Dispose()
        {
            if (_updateDelay != null) _updateDelay.Dispose();

            lock (_lock)
            {
                _appConfigurations.Clear();
                _instanceRoutes.Clear();
                _routes.Clear();
                _orderedRoutes = Array.Empty<Route>();
                _initialised = false;
            }
        }


        private void UpdateDelayElapsed(object sender, EventArgs e)
        {
            Initialise();
        }

        protected void Update(object sender, ITrakHoundModule module)
        {
            _updateDelay.Set();
        }


        public void AddApp(ITrakHoundAppConfiguration configuration)
        {
            if (configuration != null && configuration.Id != null)
            {
                lock (_lock)
                {
                    _appConfigurations.Remove(configuration.Id);
                    _appConfigurations.Add(configuration.Id, configuration);
                }

                if (AppUpdated != null) AppUpdated.Invoke(this, configuration.Id);
            }
        }

        public void RemoveApp(string configurationId)
        {
            if (configurationId != null)
            {
                lock (_lock) _appConfigurations.Remove(configurationId);
            }
        }


        public void Clear()
        {
            lock (_lock)
            {
                _appConfigurations.Clear();
                _instanceRoutes.Clear();
                _routes.Clear();
                _orderedRoutes = Array.Empty<Route>();
                _initialised = false;
            }
        }

        public void ClearPages()
        {
            lock (_lock)
            {
                _appConfigurations.Clear();
                _routes.Clear();
                _orderedRoutes = Array.Empty<Route>();
                _initialised = false;
            }
        }

        public void ReloadPages(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
  
            }
        }


        // Routers call this for every page (a Router is created for each Request), so the Routes are only built when they haven't been
        public void EnsureInitialised()
        {
            if (!_initialised) Initialise();
        }

        public void Initialise()
        {
            // Build the complete set of Routes and then swap it in, so that Match (ex. from concurrent requests)
            // never sees an empty or partially built set
            lock (_initialiseLock)
            {
                var routes = new ListDictionary<string, Route>();

                InitializeAssemblyRoutes(routes);
                InitializePackageRoutes(routes);

                _orderedRoutes = routes.Values?.OrderBy(o => o.Template, _routeComparer).ToArray() ?? Array.Empty<Route>();
                _routes = routes;
                _initialised = true;
            }
        }

        // The Instance's own (Admin) Page Routes, scanned once : the Entry Assembly's types can't change while the process runs,
        // and a new Route Manager is created on every App Load
        private static readonly Lazy<KeyValuePair<string, Route>[]> _adminRoutes = new Lazy<KeyValuePair<string, Route>[]>(ScanAdminRoutes);

        private static KeyValuePair<string, Route>[] ScanAdminRoutes()
        {
            var routes = new List<KeyValuePair<string, Route>>();

            // Not the App Host (its catch-all would match every App route)
            var pageComponentTypes = Assembly.GetEntryAssembly()?.ExportedTypes.Where(t => t.IsSubclassOf(typeof(ComponentBase)) && t != typeof(TrakHound.Instance.Pages.AppHost));
            if (pageComponentTypes != null)
            {
                foreach (var pageComponentType in pageComponentTypes)
                {
                    var routeAttributes = pageComponentType.GetCustomAttributes<RouteAttribute>();
                    if (!routeAttributes.IsNullOrEmpty())
                    {
                        foreach (var routeAttribute in routeAttributes)
                        {
                            if (!string.IsNullOrEmpty(routeAttribute.Template))
                            {
                                var newRoute = new Route
                                {
                                    Template = routeAttribute.Template,
                                    Handler = pageComponentType
                                };

                                // Admin Routes have no App ID, so each is keyed by its own ID
                                var appId = !string.IsNullOrEmpty(newRoute.AppId) ? newRoute.AppId : Guid.NewGuid().ToString();
                                routes.Add(new KeyValuePair<string, Route>(appId, newRoute));
                            }
                        }
                    }
                }
            }

            return routes.ToArray();
        }

        private void InitializeAssemblyRoutes(ListDictionary<string, Route> target)
        {
            // The Instance's own (Admin) Pages aren't available when the Admin Interface is disabled
            if (_instance?.Configuration != null && !_instance.Configuration.AdminInterfaceEnabled) return;

            lock (_lock)
            {
                if (!_loaded)
                {
                    foreach (var adminRoute in _adminRoutes.Value)
                    {
                        _instanceRoutes.Add(adminRoute.Key, adminRoute.Value);
                    }

                    _loaded = true;
                }

                foreach (var key in _instanceRoutes.Keys)
                {
                    var keyRoutes = _instanceRoutes.Get(key);
                    if (!keyRoutes.IsNullOrEmpty())
                    {
                        foreach (var keyRoute in keyRoutes)
                        {
                            target.Add(key, keyRoute);
                        }
                    }
                }
            }
        }

        //private void InitializeAssemblyRoutes()
        //{
        //    var pageComponentTypes = Assembly.GetEntryAssembly().ExportedTypes.Where(t => t.IsSubclassOf(typeof(ComponentBase)));
        //    if (pageComponentTypes != null)
        //    {
        //        var routes = new List<Route>();

        //        foreach (var pageComponentType in pageComponentTypes)
        //        {
        //            var routeAttributes = pageComponentType.GetCustomAttributes<RouteAttribute>();
        //            if (!routeAttributes.IsNullOrEmpty())
        //            {
        //                foreach (var routeAttribute in routeAttributes)
        //                {
        //                    if (!string.IsNullOrEmpty(routeAttribute.Template))
        //                    {
        //                        var newRoute = new Route
        //                        {
        //                            Template = routeAttribute.Template,
        //                            Handler = pageComponentType
        //                        };

        //                        if (!string.IsNullOrEmpty(newRoute.AppId))
        //                        {
        //                            _routes.Add(newRoute.AppId, newRoute);
        //                        }
        //                        else
        //                        {
        //                            var appId = Guid.NewGuid().ToString();
        //                            _routes.Add(appId, newRoute);
        //                        }
        //                    }
        //                }                            
        //            }
        //        }
        //    }
        //}

        private void InitializePackageRoutes(ListDictionary<string, Route> target)
        {
            List<ITrakHoundAppConfiguration> appConfigurations;
            lock (_lock) appConfigurations = _appConfigurations.Values.ToList();

            if (!appConfigurations.IsNullOrEmpty())
            {
                foreach (var appConfiguration in appConfigurations)
                {
                    var baseRoute = appConfiguration.Route;
                    if (string.IsNullOrEmpty(baseRoute)) baseRoute = "/";
                    if (!baseRoute.StartsWith('/')) baseRoute = "/" + baseRoute;

                    // Rewrite Route (when matched)
                    if (!_instance.Configuration.RouteRewrites.IsNullOrEmpty())
                    {
                        foreach (var routeRewrite in _instance.Configuration.RouteRewrites)
                        {
                            baseRoute = Url.Rewrite(baseRoute, routeRewrite.Key, routeRewrite.Value);
                        }
                    }

                    var module = _moduleManager.Get(appConfiguration.PackageId, appConfiguration.PackageVersion);
                    if (module != null)
                    {
                        // Get Client
                        var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, appConfiguration.RouterId, appConfiguration.GroupId))
                            .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.App, appConfiguration.Id, appConfiguration.Name, appConfiguration.PackageId));

                        // Get Volume
                        var volumeId = appConfiguration.VolumeId ?? appConfiguration.Id;
                        var volume = _volumeProvider.GetVolume(volumeId);

                        // Create Logger
                        var logger = _logProvider.GetLoggerByName($"app/{module.Package.Id}");

                        var defaultLayout = GetDefaultLayout(module.ModuleTypes);

                        foreach (var moduleType in module.ModuleTypes)
                        {
                            var routeAttributes = moduleType.GetCustomAttributes<RouteAttribute>();
                            if (!routeAttributes.IsNullOrEmpty())
                            {
                                foreach (var routeAttribute in routeAttributes)
                                {
                                    if (!string.IsNullOrEmpty(routeAttribute.Template))
                                    {
                                        var newRoute = new Route
                                        {
                                            AppId = appConfiguration.Id,
                                            AppName = appConfiguration.Name,
                                            Package = module.Package,
                                            Client = client,
                                            Volume = volume,
                                            Logger = logger,
                                            Parameters = appConfiguration.Parameters,
                                            BaseUrl = _baseUrl,
                                            BasePath = baseRoute,
                                            //BasePath = appConfiguration.Route,
                                            BaseLocation = $"{_packagesDirectory}/{module.PackageId}/{module.PackageVersion}",
                                            Template = $"/{Url.Combine(baseRoute, routeAttribute.Template).TrimEnd('/')}",
                                            Handler = moduleType,
                                            DefaultLayout = defaultLayout
                                        };

                                        target.Add(newRoute.Id, newRoute);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The App's "Layout" component (the same Layout the App template's Debug host uses as its Default Layout).
        /// One in the Assembly's root Namespace is preferred over one in a sub folder
        /// </summary>
        private static Type GetDefaultLayout(IEnumerable<Type> moduleTypes)
        {
            var layoutTypes = moduleTypes?.Where(o => o.Name == "Layout" && typeof(LayoutComponentBase).IsAssignableFrom(o)).ToList();
            if (layoutTypes.IsNullOrEmpty()) return null;

            return layoutTypes.FirstOrDefault(o => o.Namespace == o.Assembly.GetName().Name) ?? layoutTypes.First();
        }

        public MatchResult Match(string url)
        {
            // The Instance's own routes (ex. /_admin) are never App routes, even when an App has a route like "/{id}"
            if (!string.IsNullOrEmpty(url) && TrakHoundInstanceRoutes.IsReservedRoute("/" + url.TrimStart('/'))) return MatchResult.NoMatch();

            var routes = _orderedRoutes;
            if (routes.Length > 0)
            {
                foreach (var route in routes)
                {
                    var matchResult = route.Match(url);
                    if (matchResult.IsMatch)
                    {
                        return matchResult;
                    }
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
