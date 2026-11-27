// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Instance.Components;
using TrakHound.Instances;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Workers;

namespace TrakHound.Instance.Pages.Explorer
{
    public class ExplorerService : IDisposable
    {
        private readonly InstanceManager _instanceManager;
        private readonly Dictionary<string, ITrakHoundClientProvider> _trakhoundClientProdivers = new Dictionary<string, ITrakHoundClientProvider>();
        private readonly Dictionary<string, ITrakHoundClient> _clients = new Dictionary<string, ITrakHoundClient>(); // InstanceId:RouterId => ITrakHoundClient
        private readonly Dictionary<string, IEnumerable<ITrakHoundServiceRouterInformation>> _routers = new Dictionary<string, IEnumerable<ITrakHoundServiceRouterInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundServiceInformation>> _services = new Dictionary<string, IEnumerable<TrakHoundServiceInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundApiInformation>> _apiRoutes = new Dictionary<string, IEnumerable<TrakHoundApiInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundApiInformation>> _apiPackages = new Dictionary<string, IEnumerable<TrakHoundApiInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundAppInformation>> _apps = new Dictionary<string, IEnumerable<TrakHoundAppInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundFunctionInformation>> _functions = new Dictionary<string, IEnumerable<TrakHoundFunctionInformation>>();
        private readonly Dictionary<string, IEnumerable<TrakHoundWorkerInformation>> _workers = new Dictionary<string, IEnumerable<TrakHoundWorkerInformation>>();
        private readonly Dictionary<string, IEnumerable<string>> _logs = new Dictionary<string, IEnumerable<string>>(); // Logger Names
        private readonly Dictionary<string, IEnumerable<string>> _logDirectories = new Dictionary<string, IEnumerable<string>>();
        private readonly ListDictionary<string, TrakHoundTable> _serviceTables = new ListDictionary<string, TrakHoundTable>();
        private readonly Dictionary<string, string> _serverUrls = new Dictionary<string, string>();
        private readonly object _lock = new object();

        private IEnumerable<TrakHoundInstanceInformation> _instances;
        private bool _isLoaded;
        private int _loadVersion;


        public IEnumerable<TrakHoundInstanceInformation> Instances => _instances;

        private IEnumerable<BreadcrumbItem> _breadcrumbItems;
        public IEnumerable<BreadcrumbItem> BreadcrumbItems
        {
            get => _breadcrumbItems;
            set
            {
                _breadcrumbItems = value;
                if (PageUpdated != null) PageUpdated.Invoke(this, new EventArgs());
            }
        }

        public string SelectedInstanceId { get; set; }

        public string SelectedRouterId { get; set; }

        public EventHandler PageUpdated { get; set; }

        public EventHandler ConfigurationUpdated { get; set; }

        public EventHandler Loaded { get; set; }

        /// <summary>
        /// Raised when the Instance's Status changes (ex. Starting to Started)
        /// </summary>
        public EventHandler InstanceStatusUpdated { get; set; }

        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// True while the Instance is still loading its Modules, so the tree is not complete yet
        /// </summary>
        public bool IsInstanceStarting
        {
            get
            {
                var status = _instanceManager?.Instance?.Status;
                return status == TrakHoundInstanceStatus.Initializing || status == TrakHoundInstanceStatus.Starting;
            }
        }


        // Lists only what the signed in Admin can view (for a Role that is limited to Zones, the Modules in them)
        private readonly TrakHound.Instance.Security.AdminAccess _adminAccess;

        public ExplorerService(InstanceManager instanceManager, TrakHound.Instance.Security.AdminAccess adminAccess = null)
        {
            _adminAccess = adminAccess;
            _instanceManager = instanceManager;
            SelectedInstanceId = instanceManager.GetInstances()?.FirstOrDefault()?.Id;

            // The Admin interface is served while the Instance is still loading its Modules,
            // so reload once it has finished (otherwise the tree only shows what had loaded when it opened)
            var instance = instanceManager.Instance;
            if (instance != null)
            {
                instance.StatusUpdated += InstanceStatusChanged;
                SubscribeToModuleChanges(instance, true);
            }
        }

        public void Dispose()
        {
            var instance = _instanceManager?.Instance;
            if (instance != null)
            {
                instance.StatusUpdated -= InstanceStatusChanged;
                SubscribeToModuleChanges(instance, false);
            }

            lock (_moduleChangeLock)
            {
                _moduleChangeTimer?.Dispose();
                _moduleChangeTimer = null;
            }
        }


        // A Package install (or uninstall, or a Configuration edit) adds or removes Modules while the Instance keeps running, so its Status
        // doesn't change : reload when Modules are loaded or removed, and when Configurations change (Functions and Services are listed
        // from them). One install raises several of these, so they're combined into one reload after the last one
        private static readonly TimeSpan ModuleChangeDelay = TimeSpan.FromSeconds(1);
        private readonly object _moduleChangeLock = new object();
        private Timer _moduleChangeTimer;

        private void SubscribeToModuleChanges(ITrakHoundInstance instance, bool subscribe)
        {
            var apiProvider = instance.ApiProvider;
            var appProvider = instance.AppProvider as TrakHound.Blazor.Apps.TrakHoundAppProvider;
            var workerManager = instance.WorkerManager;
            var configurationProfile = instance.ConfigurationProfile;

            if (subscribe)
            {
                if (apiProvider != null) { apiProvider.ApiAdded += ModuleChanged; apiProvider.ApiRemoved += ModuleChanged; }
                if (appProvider != null) { appProvider.AppAdded += ModuleChanged; appProvider.AppRemoved += ModuleChanged; }
                if (workerManager != null) { workerManager.EngineAdded += ModuleChanged; workerManager.EngineRemoved += ModuleChanged; }
                if (configurationProfile != null)
                {
                    configurationProfile.ConfigurationAdded += ModuleChanged;
                    configurationProfile.ConfigurationRemoved += ModuleChanged;
                    configurationProfile.ConfigurationChanged += ModuleChanged;
                }
            }
            else
            {
                if (apiProvider != null) { apiProvider.ApiAdded -= ModuleChanged; apiProvider.ApiRemoved -= ModuleChanged; }
                if (appProvider != null) { appProvider.AppAdded -= ModuleChanged; appProvider.AppRemoved -= ModuleChanged; }
                if (workerManager != null) { workerManager.EngineAdded -= ModuleChanged; workerManager.EngineRemoved -= ModuleChanged; }
                if (configurationProfile != null)
                {
                    configurationProfile.ConfigurationAdded -= ModuleChanged;
                    configurationProfile.ConfigurationRemoved -= ModuleChanged;
                    configurationProfile.ConfigurationChanged -= ModuleChanged;
                }
            }
        }

        private void ModuleChanged<T>(object sender, T args)
        {
            // Only if the Explorer has been opened (otherwise it loads when it opens), and not while the Instance is starting
            // (the Status reload covers that)
            if (!_isLoaded || IsInstanceStarting) return;

            lock (_moduleChangeLock)
            {
                _moduleChangeTimer ??= new Timer(ReloadAfterModuleChange);
                _moduleChangeTimer.Change(ModuleChangeDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private async void ReloadAfterModuleChange(object state)
        {
            try
            {
                var instanceId = _instanceManager?.Instance?.Id;
                if (!string.IsNullOrEmpty(instanceId)) await LoadInstance(instanceId, true);
            }
            catch { } // Runs on a Timer thread, so never let a reload failure reach it
        }

        private async void InstanceStatusChanged(string instanceId, TrakHoundInstanceStatus status)
        {
            try
            {
                if (status == TrakHoundInstanceStatus.Started || status == TrakHoundInstanceStatus.Error)
                {
                    // Only if the Explorer has been opened (otherwise it loads when it opens)
                    if (_isLoaded) await LoadInstance(instanceId, true);
                }

                InstanceStatusUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch { } // Raised on the Instance's thread, so never let a reload failure reach it
        }


        public async Task LoadInstance(string instanceId, bool forceReload = false)
        {
            if (_instanceManager != null && !string.IsNullOrEmpty(instanceId))
            {
                if (!_isLoaded || forceReload)
                {
                    _isLoaded = true;

                    // A load started during Startup can finish after the reload when it is Started, so only the latest is kept
                    var loadVersion = Interlocked.Increment(ref _loadVersion);

                    var instance = _instanceManager.GetInstance(instanceId);
                    if (instance != null)
                    {
                        var client = _instanceManager.GetClient(instanceId);
                        if (client != null)
                        {
                            var instances = _instanceManager.GetInstances();
                            // Independent requests, so run them concurrently
                            var routersTask = client.Services.Routers.GetRouters();
                            var servicesTask = client.Services.Drivers.GetDrivers();
                            var apiRouteInformationsTask = client.Api.GetRouteInformation();
                            var appInformationsTask = client.Apps.GetInformation();
                            var functionsTask = client.Functions.GetInformation();
                            var workersTask = client.Workers.GetInformation();
                            var logsTask = GetLoggerNames(client);
                            var logDirectoriesTask = client.Logs.ListDirectories("*");

                            await Task.WhenAll(routersTask, servicesTask, apiRouteInformationsTask, appInformationsTask, functionsTask, workersTask, logsTask, logDirectoriesTask);

                            var routers = await routersTask;
                            var services = await servicesTask;
                            var apiRouteInformations = await apiRouteInformationsTask;
                            var appInformations = await appInformationsTask;
                            var functions = await functionsTask;
                            var workers = await workersTask;
                            var logs = await logsTask;
                            var logDirectories = await logDirectoriesTask;

                            lock (_lock)
                            {
                                if (loadVersion != _loadVersion) return;

                                _instances = instances;

                                _clients.Remove(instance.Id);
                                _clients.Add(instance.Id, client);

                                _routers.Remove(instance.Id);
                                _routers.Add(instance.Id, routers);

                                _services.Remove(instance.Id);
                                _services.Add(instance.Id, services);

                                _apps.Remove(instance.Id);
                                _apps.Add(instance.Id, appInformations);

                                _apiRoutes.Remove(instance.Id);
                                _apiRoutes.Add(instance.Id, apiRouteInformations);

                                _functions.Remove(instance.Id);
                                _functions.Add(instance.Id, functions);

                                _workers.Remove(instance.Id);
                                _workers.Add(instance.Id, workers);

                                _logs.Remove(instance.Id);
                                _logs.Add(instance.Id, logs);

                                _logDirectories.Remove(instance.Id);
                                _logDirectories.Add(instance.Id, logDirectories);
                            }

                            if (ConfigurationUpdated != null) ConfigurationUpdated.Invoke(this, new EventArgs());
                        }
                    }
                }
            }
        }

        public async Task LoadServiceTables(string instanceId, string tableServiceId)
        {
            if (_instanceManager != null && !string.IsNullOrEmpty(instanceId) && !string.IsNullOrEmpty(tableServiceId))
            {
                var instance = _instanceManager.GetInstance(instanceId);
                if (instance != null)
                {
                    var client = _instanceManager.GetClient(instanceId);
                    if (client != null)
                    {
                        var tableKey = $"{instanceId}:{tableServiceId}";
                        var tables = await client.Services.Tables.List(tableServiceId);

                        lock (_lock)
                        {
                            _serviceTables.Remove(tableKey);
                            _serviceTables.Add(tableKey, tables.Result);
                        }

                        if (ConfigurationUpdated != null) ConfigurationUpdated.Invoke(this, new EventArgs());
                    }
                }
            }
        }


        public TrakHoundInstanceInformation GetInstance(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                return _instances?.FirstOrDefault(o => o.Id == instanceId);
            }

            return null;
        }

        public IEnumerable<ITrakHoundServiceRouterInformation> GetRouters(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_routers.ContainsKey(instanceId))
                {
                    return _routers[instanceId];
                }
            }

            return null;
        }

        public ITrakHoundServiceRouterInformation GetRouter(string instanceId, string routerId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_routers.ContainsKey(instanceId))
                {
                    var routers = _routers[instanceId];
                    return routers?.FirstOrDefault(o => o.Id == routerId);
                }
            }

            return null;
        }

        private IEnumerable<T> Visible<T>(IEnumerable<T> modules, string type, Func<T, string> getConfigurationId)
        {
            if (modules == null || _adminAccess == null || !_adminAccess.IsLimitedToZones(TrakHound.Instance.Security.TrakHoundAdminSecurity.Modules)) return modules;

            return modules.Where(o => _adminAccess.CanViewModule(type, getConfigurationId(o))).ToList();
        }

        public IEnumerable<TrakHoundServiceInformation> GetServices(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_services.ContainsKey(instanceId))
                {
                    return _services[instanceId]?.Where(o => _adminAccess == null || _adminAccess.CanViewService(o.ConfigurationId)).ToList();
                }
            }

            return null;
        }

        public TrakHoundServiceInformation GetServices(string instanceId, string serviceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_services.ContainsKey(instanceId))
                {
                    var services = _services[instanceId];
                    return services?.FirstOrDefault(o => o.ConfigurationId == serviceId);
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundApiInformation> GetApiRoutes(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_apiRoutes.ContainsKey(instanceId))
                {
                    return Visible(_apiRoutes[instanceId], TrakHound.Zones.TrakHoundZoneResources.ApiPrefix, o => o.Id);
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundApiInformation> GetApiPackages(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_apiPackages.ContainsKey(instanceId))
                {
                    return _apiPackages[instanceId];
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundAppInformation> GetApps(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_apps.ContainsKey(instanceId))
                {
                    return Visible(_apps[instanceId], TrakHound.Zones.TrakHoundZoneResources.AppPrefix, o => o.Id);
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundFunctionInformation> GetFunctions(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_functions.ContainsKey(instanceId))
                {
                    return Visible(_functions[instanceId], TrakHound.Zones.TrakHoundZoneResources.FunctionPrefix, o => o.ConfigurationId);
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundWorkerInformation> GetWorkers(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_workers.ContainsKey(instanceId))
                {
                    return Visible(_workers[instanceId], TrakHound.Zones.TrakHoundZoneResources.WorkerPrefix, o => o.ConfigurationId);
                }
            }

            return null;
        }

        /// <summary>
        /// Logger Names (ex. "system/TrakHound.Instance.InstanceApplication")
        /// </summary>
        public IEnumerable<string> GetLogs(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_logs.ContainsKey(instanceId))
                {
                    return _logs[instanceId];
                }
            }

            return null;
        }

        private static async Task<IEnumerable<string>> GetLoggerNames(ITrakHoundClient client)
        {
            // Logger Names are readable paths (List() returns Logger IDs, which are hashes for most Loggers).
            // Null if the Client can't list Loggers (the Explorer then uses the Log Directories)
            var loggers = await client.Logs.QueryLoggers("*");
            return loggers?.Select(o => o.Name).Where(o => !string.IsNullOrEmpty(o)).ToList();
        }

        public IEnumerable<string> GetLogDirectories(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_logDirectories.ContainsKey(instanceId))
                {
                    return _logDirectories[instanceId];
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundTable> GetServiceTables(string instanceId, string serviceId)
        {
            if (!string.IsNullOrEmpty(instanceId) && !string.IsNullOrEmpty(serviceId))
            {
                var tableKey = $"{instanceId}:{serviceId}";
                return _serviceTables.Get(tableKey);
            }

            return null;
        }

        public TrakHoundTable GetServiceTable(string instanceId, string serviceId, string tableId)
        {
            if (!string.IsNullOrEmpty(instanceId) && !string.IsNullOrEmpty(serviceId) && !string.IsNullOrEmpty(tableId))
            {
                var tableKey = $"{instanceId}:{serviceId}";
                var tables = _serviceTables.Get(tableKey);
                if (!tables.IsNullOrEmpty())
                {
                    return tables.FirstOrDefault(o => o.Id == tableId);
                }
            }

            return null;
        }

        public TrakHoundInstanceInformation GetServer(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId) && !_instances.IsNullOrEmpty())
            {
                return _instances.FirstOrDefault(o => o.Id == instanceId);
            }

            return null;
        }

        public string GetServerUrl(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_serverUrls.ContainsKey(instanceId))
                {
                    return _serverUrls[instanceId];
                }
            }

            return null;
        }


        public ITrakHoundClient GetClient(string instanceId, string routerId = null)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                lock (_lock)
                {
                    return _clients.GetValueOrDefault(instanceId);
                }
            }

            return null;
        }


        //public ITrakHoundClient GetClient(string instanceId, string routerId)
        //{
        //    if (!string.IsNullOrEmpty(instanceId))
        //    {
        //        if (_trakhoundClientProdivers.ContainsKey(instanceId))
        //        {
        //            var clientProvider = _trakhoundClientProdivers[instanceId];
        //            if (clientProvider != null)
        //            {
        //                var client = clientProvider.GetClient(routerId);
        //                //client.RouterId = routerId;

        //                return client;
        //            }
        //        }
        //    }

        //    return null;
        //}

        public ITrakHoundClientProvider GetClientProvider(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (_trakhoundClientProdivers.ContainsKey(instanceId))
                {
                    return _trakhoundClientProdivers[instanceId];
                }
            }

            return null;
        }
    }
}
