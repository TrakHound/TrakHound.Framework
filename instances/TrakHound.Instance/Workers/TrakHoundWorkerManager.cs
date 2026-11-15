// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Workers
{
    public class TrakHoundWorkerManager : IAsyncDisposable
    {
        private const string _packageCategory = "worker";

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly Dictionary<string, WorkerItem> _workers = new Dictionary<string, WorkerItem>();
        private readonly Dictionary<string, TrakHoundWorkerEngine> _engines = new Dictionary<string, TrakHoundWorkerEngine>(); // Worker.Id => Engine
        private readonly Dictionary<string, ITrakHoundLogger> _loggers = new Dictionary<string, ITrakHoundLogger>(); // Worker.Id => Logger
        private readonly Dictionary<string, string> _configurationEngines = new Dictionary<string, string>(); // Configuration.Id => Worker.Id
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(2000);
        private readonly TrakHoundWorkerMetricsCollector _metrics = new TrakHoundWorkerMetricsCollector();
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundWorkerManager>();
        private readonly object _lock = new object();

        private readonly Dictionary<string, string> _installedConfigurations = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash and the Router it resolves to
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(); // Configuration.Id => Module.Hash

        private bool _started;
        private bool _disposed;


        public IEnumerable<TrakHoundWorkerEngine> Engines => GetEngines();

        /// <summary>
        /// Record Worker Metrics (Status changes, restarts, errors)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;

        /// <summary>
        /// Measure the CPU usage of each Worker
        /// </summary>
        public bool CpuMonitoringEnabled { get; set; } = true;

        public event EventHandler<TrakHoundWorkerEngine> EngineAdded;

        public event EventHandler<string> EngineRemoved;

        public event TrakHoundWorkerStatusHandler StatusReceived;

        public event TrakHoundWorkerLogHandler LogReceived;


        class WorkerItem
        {
            public ITrakHoundModule Module { get; set; }
            public ITrakHoundWorkerConfiguration Configuration { get; set; }
            public string VolumeId { get; set; }
            public TrakHoundWorkerStatusType Status { get; set; }
            public DateTime LastUpdated { get; set; }
        }


        public TrakHoundWorkerManager(
            ITrakHoundInstance instance,
            TrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundClientProvider clientProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            TrakHoundPackageManager packageManager
            )
        {
            _instance = instance;

            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ConfigurationAdded;
            _configurationProfile.ConfigurationRemoved += ConfigurationRemoved;

            _packageManager = packageManager;
            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;

            _moduleManager = moduleProvider.Get<ITrakHoundWorker>(_packageCategory);

            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;

            _delayLoadEvent.Elapsed += LoadDelayElapsed;
        }

        public async ValueTask DisposeAsync()
        {
            // A Load still in progress must not start an Engine after this (see LoadConfiguration)
            lock (_lock) _disposed = true;

            // The Configuration Profile and Package Manager outlive this Manager (a new one is created each time the Instance starts)
            _configurationProfile.ConfigurationAdded -= ConfigurationAdded;
            _configurationProfile.ConfigurationRemoved -= ConfigurationRemoved;
            _packageManager.PackageAdded -= PackageAdded;
            _packageManager.PackageRemoved -= PackageRemoved;

            // Dispose in parallel so a slow Worker doesn't delay the others
            await Task.WhenAll(GetEngines().Select(o => o.DisposeAsync().AsTask()));

            _delayLoadEvent.Elapsed -= LoadDelayElapsed;
            _delayLoadEvent.Dispose();

            lock (_lock)
            {
                _workers.Clear();
                _engines.Clear();
                _loggers.Clear();
                _configurationEngines.Clear();
                _installedConfigurations.Clear();
                _installedModuleHashes.Clear();
            }
        }


        private void PackageAdded(object sender, TrakHoundPackage package)
        {
            if (package != null && package.Category == _packageCategory)
            {
                _delayLoadEvent.Set();
            }
        }

        private void PackageRemoved(object sender, TrakHoundPackageChangedArgs args)
        {
            if (args.PackageCategory == _packageCategory)
            {
                _delayLoadEvent.Set();
            }
        }


        // async void : an exception from Worker code would end the process, so nothing may escape
        private async void ConfigurationAdded(object sender, ITrakHoundConfiguration configuration)
        {
            try
            {
                if (configuration.Category == TrakHoundWorkerConfiguration.ConfigurationCategory)
                {
                    await AddConfiguration((ITrakHoundWorkerConfiguration)configuration);
                }
                else if (configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
                {
                    // Reload the Workers whose Router came from the Group
                    _delayLoadEvent.Set();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Adding Worker Configuration ({configuration?.Id}) : {ex.Message}");
            }
        }

        // async void : nothing may escape
        private async void ConfigurationRemoved(object sender, ITrakHoundConfiguration configuration)
        {
            try
            {
                if (configuration.Category == TrakHoundWorkerConfiguration.ConfigurationCategory)
                {
                    await RemoveConfiguration(configuration.Id);
                }
                else if (configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
                {
                    // Members fall back to the Default Router
                    _delayLoadEvent.Set();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Removing Worker Configuration ({configuration?.Id}) : {ex.Message}");
            }
        }


        public void StartAll()
        {
            foreach (var engine in GetEngines())
            {
                engine.Start();
            }

            _started = true;
        }

        public void Start(string engineId)
        {
            GetEngine(engineId)?.Start();
        }

        public async Task StopAll()
        {
            // Stop in parallel so a slow Worker doesn't delay the others
            await Task.WhenAll(GetEngines().Select(o => o.Stop()));

            _started = false;
        }

        public async Task Stop(string engineId)
        {
            var engine = GetEngine(engineId);
            if (engine != null)
            {
                await engine.Stop();
            }
        }

        private List<TrakHoundWorkerEngine> GetEngines()
        {
            lock (_lock) return _engines.Values.ToList();
        }


        #region "Information"

        public IEnumerable<TrakHoundWorkerInformation> GetInformation()
        {
            var informations = new List<TrakHoundWorkerInformation>();

            IEnumerable<WorkerItem> items;
            lock (_lock) items = _workers.Values.ToList();
            if (!items.IsNullOrEmpty())
            {
                foreach (var item in items)
                {
                    var information = TrakHoundWorkerInformation.Create(item.Configuration, item.Module.ModuleTypes?.FirstOrDefault(), item.Module.Package, item.VolumeId, item.Module.PackageReadMe);
                    if (information != null)
                    {
                        information.Id = GenerateWorkerId(_instance.Id, item.Module.Package.Id, item.Configuration.Id);
                        information.VolumeId = item.VolumeId;
                        information.Status = item.Status;
                        information.LastUpdated = item.LastUpdated;
                        SetCpuUsage(information);

                        informations.Add(information);
                    }
                }
            }

            return informations;
        }

        public IEnumerable<TrakHoundWorkerCpuSample> GetCpuHistory(string workerId, DateTime? from = null)
        {
            var engine = GetEngine(workerId);
            return engine?.GetCpuHistory(from);
        }

        private void SetCpuUsage(TrakHoundWorkerInformation information)
        {
            var engine = GetEngine(information.Id);
            if (engine != null)
            {
                information.CpuUsage = engine.CpuUsage;
                information.CpuTime = engine.TotalProcessorTime.TotalMilliseconds;
            }
        }

        public TrakHoundWorkerInformation GetInformation(string workerId)
        {
            TrakHoundWorkerInformation information = null;

            if (!string.IsNullOrEmpty(workerId))
            {
                var workerKey = workerId.ToLower();

                WorkerItem item;
                lock (_lock) item = _workers.GetValueOrDefault(workerKey);
                if (item != null)
                {
                    information = TrakHoundWorkerInformation.Create(item.Configuration, item.Module.ModuleTypes?.FirstOrDefault(), item.Module.Package, item.VolumeId, item.Module.PackageReadMe);
                    if (information != null)
                    {
                        information.Id = GenerateWorkerId(_instance.Id, item.Module.Package.Id, item.Configuration.Id);
                        information.VolumeId = item.VolumeId;
                        information.Status = item.Status;
                        information.LastUpdated = item.LastUpdated;
                        SetCpuUsage(information);
                    }
                }
            }

            return information;
        }

        public IEnumerable<TrakHoundWorkerInformation> GetInformation(IEnumerable<string> workerIds)
        {
            if (!workerIds.IsNullOrEmpty())
            {
                var informations = new List<TrakHoundWorkerInformation>();

                foreach (var workerId in workerIds)
                {
                    var information = GetInformation(workerId);
                    if (information != null) informations.Add(information);
                }

                return informations;
            }

            return null;
        }

        #endregion

        #region "Metrics"

        /// <summary>
        /// Gets the Metrics for each Worker that has been loaded
        /// </summary>
        public IEnumerable<TrakHoundWorkerMetrics> GetMetrics()
        {
            var metrics = _metrics.GetMetrics();
            foreach (var workerMetrics in metrics) SetCpuMetrics(workerMetrics);
            return metrics;
        }

        /// <summary>
        /// Gets the Metrics, including the Recent Status changes, for the specified Worker (null if it is unknown)
        /// </summary>
        public TrakHoundWorkerMetrics GetMetrics(string workerId)
        {
            var metrics = _metrics.GetMetrics(workerId);
            if (metrics != null) SetCpuMetrics(metrics);
            return metrics;
        }

        private void SetCpuMetrics(TrakHoundWorkerMetrics metrics)
        {
            metrics.CpuWindow = (int)TrakHoundWorkerEngine.CpuHistoryDuration.TotalSeconds;

            var engine = GetEngine(metrics.WorkerId);
            if (engine != null)
            {
                metrics.CpuUsage = Math.Round(engine.CpuUsage, 2);
                metrics.CpuTime = Math.Round(engine.TotalProcessorTime.TotalMilliseconds, 0);

                var samples = engine.GetCpuHistory();
                if (!samples.IsNullOrEmpty())
                {
                    metrics.AverageCpuUsage = Math.Round(samples.Average(o => o.Usage), 2);
                    metrics.MaxCpuUsage = Math.Round(samples.Max(o => o.Usage), 2);
                }
            }
        }

        #endregion


        /// <summary>
        /// Gets the Configuration of the Worker with the specified Worker ID (null if it is not loaded)
        /// </summary>
        public ITrakHoundWorkerConfiguration GetConfiguration(string workerId)
        {
            if (string.IsNullOrEmpty(workerId)) return null;

            lock (_lock) return _workers.GetValueOrDefault(workerId.ToLower())?.Configuration;
        }

        public TrakHoundWorkerEngine GetEngine(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var workerKey = workerId.ToLower();

                TrakHoundWorkerEngine engine;
                lock (_lock) engine = _engines.GetValueOrDefault(workerKey);
                if (engine != null)
                {
                    return engine;
                }
            }

            return null;
        }


        public async Task AddConfiguration(ITrakHoundWorkerConfiguration configuration)
        {
            if (configuration != null)
            {
                var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                if (module != null)
                {
                    await LoadConfiguration(module, configuration, true);
                }
            }
        }

        public async Task RemoveConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                string workerId;
                lock (_lock) workerId = _configurationEngines.GetValueOrDefault(configurationId);
                if (workerId != null)
                {
                    // Stop the Engine and Dispose of the Worker
                    var existingEngine = GetEngine(workerId);
                    if (existingEngine != null) await existingEngine.DisposeAsync();

                    var workerKey = workerId.ToLower();

                    lock (_lock)
                    {
                        _workers.Remove(workerKey);
                        _engines.Remove(workerKey);
                        _loggers.Remove(workerKey);
                        _configurationEngines.Remove(configurationId);
                        _installedConfigurations.Remove(configurationId);
                        _installedModuleHashes.Remove(configurationId);
                    }

                    if (EngineRemoved != null) EngineRemoved.Invoke(this, workerId);
                }
            }
        }


        // async void : nothing may escape
        private async void LoadDelayElapsed(object sender, EventArgs args)
        {
            try
            {
                await Load();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Loading Workers : {ex.Message}");
            }
        }

        public async Task Load()
        {
            var configurations = _configurationProfile.Get<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    if (!string.IsNullOrEmpty(configuration.Id))
                    {
                        var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                        if (module != null)
                        {
                            // Get the Installed Hash (to check if the configuration has changed)
                            string installedHash;
                            string installedModuleHash;
                            lock (_lock)
                            {
                                installedHash = _installedConfigurations.GetValueOrDefault(configuration.Id);
                                installedModuleHash = _installedModuleHashes.GetValueOrDefault(configuration.Id);
                            }

                            if (GetLoadHash(configuration) != installedHash || module.Package.Hash != installedModuleHash)
                            {
                                await LoadConfiguration(module, configuration, _started);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The Configuration's Hash and the Router it resolves to (so the Worker reloads when its Group's Router changes)
        /// </summary>
        private string GetLoadHash(ITrakHoundWorkerConfiguration configuration)
        {
            return TrakHoundGroups.GetLoadHash(_configurationProfile, configuration.Hash, configuration.RouterId, configuration.GroupId);
        }

        private async Task LoadConfiguration(ITrakHoundModule module, ITrakHoundWorkerConfiguration configuration, bool startEngine = false)
        {
            // Read before the Client's Router, so a Group change in between reloads the Worker again (instead of being missed)
            var loadHash = GetLoadHash(configuration);

            // Get Client
            var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, configuration.RouterId, configuration.GroupId))
                .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.Worker, configuration.Id, configuration.Name, configuration.PackageId));

            // Get Volume
            var volumeId = configuration.VolumeId ?? configuration.Id;
            var volume = _volumeProvider.GetVolume(volumeId);

            // Get Variables
            var variables = _variableProvider.GetCollection(configuration.Id);

            var worker = CreateInstance(module, configuration);
            //var worker = CreateInstance(module, configuration, volume, variables);
            if (worker != null)
            {
                worker.Id = GenerateWorkerId(_instance.Id, configuration.PackageId, configuration.Id);
                worker.InstanceId = _instance.Id;
                worker.Package = module.Package;
                worker.SetClient(client);
                worker.SetVolume(volume);
                worker.SetVariables(variables);

                var now = DateTime.UtcNow;

                // Stop any existing Engine with the same Worker ID and Dispose of its Worker (it is replaced below)
                var existingEngine = GetEngine(worker.Id);
                if (existingEngine != null) await existingEngine.DisposeAsync();

                var workerItem = new WorkerItem();
                workerItem.Module = module;
                workerItem.Configuration = configuration;
                workerItem.VolumeId = volumeId;
                workerItem.LastUpdated = now;

                var engineLoggerName = $"workers/{module.PackageId}/{worker.Id}/Engine";
                var engineLogger = _logProvider.GetLoggerByName(engineLoggerName);

                if (MetricsEnabled) _metrics.Register(worker.Id, configuration.Id, !string.IsNullOrEmpty(configuration.Name) ? configuration.Name : module.PackageId, module.PackageId, module.PackageVersion);

                var cpuMonitor = CpuMonitoringEnabled ? TrakHoundModuleCpuMonitors.Default.GetMonitor(TrakHound.Instances.TrakHoundCpuModuleTypes.Worker, worker.Id, !string.IsNullOrEmpty(configuration.Name) ? configuration.Name : module.PackageId) : null;
                var engine = new TrakHoundWorkerEngine(worker, module.PackageId, module.PackageVersion, CpuMonitoringEnabled, cpuMonitor);
                engine.StatusReceived += WorkerStatusReceivedHandler;
                engine.LogReceived += WorkerLogReceivedHandler;

                var workerKey = worker.Id.ToLower();

                bool disposed;
                lock (_lock)
                {
                    disposed = _disposed;
                    if (!disposed)
                    {
                        _workers.Remove(workerKey);
                        _workers.Add(workerKey, workerItem);

                        _engines.Remove(workerKey);
                        _engines.Add(workerKey, engine);

                        _loggers.Remove(workerKey);
                        _loggers.Add(workerKey, engineLogger);

                        _configurationEngines.Remove(configuration.Id);
                        _configurationEngines.Add(configuration.Id, worker.Id);

                        _installedConfigurations.Remove(configuration.Id);
                        _installedModuleHashes.Remove(configuration.Id);

                        // Add to installed List
                        _installedConfigurations.Add(configuration.Id, loadHash);
                        _installedModuleHashes.Add(configuration.Id, module.Package.Hash);
                    }
                }

                if (disposed)
                {
                    // The Manager was disposed while the Worker was loading (ex. the Instance stopped)
                    await engine.DisposeAsync();
                    return;
                }

                if (EngineAdded != null) EngineAdded.Invoke(this, engine);

                if (startEngine) engine.Start();
            }
        }


        private void WorkerStatusReceivedHandler(string workerId, TrakHoundWorkerStatusType status)
        {
            if (workerId != null)
            {
                var timestamp = DateTime.UtcNow;

                lock (_lock)
                {
                    var workerKey = workerId.ToLower();
                    var item = _workers.GetValueOrDefault(workerKey);
                    if (item != null)
                    {
                        item.Status = status;
                        item.LastUpdated = timestamp;
                    }
                }

                if (MetricsEnabled) _metrics.StatusReceived(workerId, status);

                if (StatusReceived != null) StatusReceived.Invoke(workerId, status);
            }
        }

        private void WorkerLogReceivedHandler(string workerId, TrakHoundLogItem logItem)
        {
            if (workerId != null)
            {
                var timestamp = DateTime.UtcNow;

                lock (_lock)
                {
                    var workerKey = workerId.ToLower();
                    var logger = _loggers.GetValueOrDefault(workerKey);
                    if (logger != null)
                    {
                        logger.Log(logItem);
                    }
                }

                if (MetricsEnabled) _metrics.LogReceived(workerId, logItem);

                if (LogReceived != null) LogReceived.Invoke(workerId, logItem);
            }
        }

        private static TrakHoundWorker CreateInstance(ITrakHoundModule module, ITrakHoundWorkerConfiguration configuration)
        {
            if (module != null && module.ModuleTypes != null && configuration != null)
            {
                try
                {
                    var constructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] {
                         typeof(ITrakHoundWorkerConfiguration)
                         });
                    if (constructor != null)
                    {
                        return (TrakHoundWorker)constructor.Invoke(new object[] { configuration });
                    }
                    else
                    {
                        // Should only be needed temporarily. 6/15/2026
                        var backwardsCompatibleConstructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] {
                            typeof(ITrakHoundWorkerConfiguration),
                            typeof(ITrakHoundClient),
                            typeof(ITrakHoundVolume)
                         });
                        if (backwardsCompatibleConstructor != null)
                        {
                            ITrakHoundClient client = null;
                            ITrakHoundVolume volume = null;

                            return (TrakHoundWorker)backwardsCompatibleConstructor.Invoke(new object[] { configuration, client, volume });
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        //private ITrakHoundWorker CreateInstance(ITrakHoundModule module, ITrakHoundWorkerConfiguration configuration, ITrakHoundVolume volume, ITrakHoundVariableCollection variables)
        //{
        //    if (module != null && module.ModuleTypes != null && configuration != null)
        //    {
        //        try
        //        {
        //            var constructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] {
        //                 typeof(ITrakHoundWorkerConfiguration),
        //                 typeof(ITrakHoundClient),
        //                 typeof(ITrakHoundVolume),
        //                 typeof(ITrakHoundVariableCollection)
        //                 });
        //            if (constructor != null)
        //            {
        //                var client = _clientProvider.GetClient(configuration.RouterId);

        //                if (client != null && volume != null)
        //                {
        //                    var worker = (ITrakHoundWorker)constructor.Invoke(new object[] { configuration, client, volume, variables });
        //                    if (worker != null)
        //                    {
        //                        ((TrakHoundWorker)worker).InstanceId = _instance.Id;
        //                        ((TrakHoundWorker)worker).Package = module.Package;
        //                        ((TrakHoundWorker)worker).Id = GenerateWorkerId(_instance.Id, configuration.PackageId, configuration.Id);

        //                        return worker;
        //                    }
        //                }
        //            }
        //        }
        //        catch { }
        //    }

        //    return null;
        //}

        private static string GenerateWorkerId(string instanceId, string packageId, string configurationId)
        {
            return $"{instanceId}:{packageId}:{configurationId}".ToMD5Hash();
        }
    }
}
