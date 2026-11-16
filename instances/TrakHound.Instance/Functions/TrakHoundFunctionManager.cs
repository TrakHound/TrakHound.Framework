// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

namespace TrakHound.Functions
{
    public delegate void TrakHoundFunctionStatusHandler(string runId, TrakHoundFunctionStatusType status);
    public delegate void TrakHoundFunctionLogHandler(string runId, TrakHoundLogItem logItem);

    public class TrakHoundFunctionManager : IDisposable
    {
        private const string _packageCategory = "function";
        private const string _instancesBasePath = "TrakHound:/.Instances";
        private const string _functionsBasePath = "Functions";
        private const string _logsPath = "Logs";

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly Dictionary<string, FunctionItem> _functions = new Dictionary<string, FunctionItem>(); // FunctionId => FunctionItem

        // A copy of _functions, replaced (never changed) when a Function is loaded or removed, so the frequent reads
        // (ex. GetInformation for every Functions Api Request) don't take the lock
        private volatile Dictionary<string, FunctionItem> _functionsSnapshot = new Dictionary<string, FunctionItem>();
        private readonly Dictionary<string, TrakHoundFunctionEngine> _engines = new Dictionary<string, TrakHoundFunctionEngine>(); // RunId => FunctionEngine
        private readonly Dictionary<string, string> _configurationFunctions = new Dictionary<string, string>(); // Configuration.Id => Function.Id
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(2000);
        private readonly TrakHoundFunctionMetricsCollector _metrics = new TrakHoundFunctionMetricsCollector();

        // Weak keys so an unloaded Package's types can be collected
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, IReadOnlyList<TrakHoundFunctionParameterInformation>> _parameterDefinitions = new System.Runtime.CompilerServices.ConditionalWeakTable<Type, IReadOnlyList<TrakHoundFunctionParameterInformation>>();
        private readonly object _lock = new object();

        private readonly Dictionary<string, string> _installedConfigurations = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash and the Router it resolves to
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(); // Configuration.Id => Module.Hash


        public IEnumerable<TrakHoundFunctionEngine> Engines
        {
            get { lock (_lock) return _engines.Values.ToList(); }
        }

        /// <summary>
        /// Record Function Run Metrics
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;

        /// <summary>
        /// Measure the CPU time used by each Function Run
        /// </summary>
        public bool CpuMonitoringEnabled { get; set; } = true;


        public event EventHandler<TrakHoundFunctionEngine> EngineAdded;

        public event EventHandler<string> EngineRemoved;

        public event TrakHoundFunctionStatusHandler StatusUpdated;

        public event TrakHoundFunctionLogHandler LogUpdated;


        class FunctionItem
        {
            public ITrakHoundModule Module { get; set; }
            public ITrakHoundFunctionConfiguration Configuration { get; set; }
            public string VolumeId { get; set; }
        }


        public TrakHoundFunctionManager(
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

            _moduleManager = moduleProvider.Get<ITrakHoundFunction>(_packageCategory);

            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;

            _delayLoadEvent.Elapsed += LoadDelayElapsed;
        }

        public void Dispose()
        {
            // The Configuration Profile and Package Manager outlive this Manager (a new one is created each time the Instance starts)
            _configurationProfile.ConfigurationAdded -= ConfigurationAdded;
            _configurationProfile.ConfigurationRemoved -= ConfigurationRemoved;
            _packageManager.PackageAdded -= PackageAdded;
            _packageManager.PackageRemoved -= PackageRemoved;

            // Dispose outside the lock (a Run removes its Engine within the lock when it completes)
            foreach (var engine in Engines)
            {
                engine.Dispose();
            }

            _delayLoadEvent.Elapsed -= LoadDelayElapsed;
            _delayLoadEvent.Dispose();

            lock (_lock)
            {
                _functions.Clear();
                _functionsSnapshot = new Dictionary<string, FunctionItem>();
                _engines.Clear();
                _configurationFunctions.Clear();
                _installedConfigurations.Clear();
                _installedModuleHashes.Clear();
            }
        }


        public TrakHoundFunctionEngine GetEngine(string runId)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                lock (_lock)
                {
                    return _engines.GetValueOrDefault(runId);
                }
            }

            return null;
        }

        /// <summary>
        /// Request that a running Function stop. Returns false if no Function is running with the specified RunId.
        /// </summary>
        public bool CancelRun(string runId)
        {
            var engine = GetEngine(runId);
            if (engine != null)
            {
                engine.Cancel();
                return true;
            }

            return false;
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


        private void ConfigurationAdded(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration.Category == TrakHoundFunctionConfiguration.ConfigurationCategory)
            {
                AddConfiguration((ITrakHoundFunctionConfiguration)configuration);
            }
            else if (configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
            {
                // Reload the Functions whose Router came from the Group
                _delayLoadEvent.Set();
            }
        }

        private void ConfigurationRemoved(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration.Category == TrakHoundFunctionConfiguration.ConfigurationCategory)
            {
                RemoveConfiguration(configuration.Id);
            }
            else if (configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
            {
                // Members fall back to the Default Router
                _delayLoadEvent.Set();
            }
        }


        #region "Information"

        public IEnumerable<TrakHoundFunctionInformation> GetInformation()
        {
            var informations = new List<TrakHoundFunctionInformation>();

            var functionItems = _functionsSnapshot.Values;
            if (!functionItems.IsNullOrEmpty())
            {
                foreach (var functionItem in functionItems)
                {
                    var information = TrakHoundFunctionInformationFactory.Create(functionItem.Configuration, functionItem.Module.ModuleTypes?.FirstOrDefault(), functionItem.Module.Package, functionItem.VolumeId, functionItem.Module.PackageReadMe);
                    if (information != null)
                    {
                        informations.Add(information);
                    }
                }
            }

            return informations;
        }

        public TrakHoundFunctionInformation GetInformation(string functionId)
        {
            TrakHoundFunctionInformation information = null;

            if (!string.IsNullOrEmpty(functionId))
            {
                var functionKey = functionId.ToLower();

                var functionItem = _functionsSnapshot.GetValueOrDefault(functionKey);
                if (functionItem != null)
                {
                    information = TrakHoundFunctionInformationFactory.Create(functionItem.Configuration, functionItem.Module.ModuleTypes?.FirstOrDefault(), functionItem.Module.Package, functionItem.VolumeId, functionItem.Module.PackageReadMe);
                }
            }     

            return information;
        }

        /// <summary>
        /// Gets the Configuration of the Function with the specified Function ID (null if it is not loaded)
        /// </summary>
        public ITrakHoundFunctionConfiguration GetConfiguration(string functionId)
        {
            if (string.IsNullOrEmpty(functionId)) return null;

            return _functionsSnapshot.GetValueOrDefault(functionId.ToLower())?.Configuration;
        }

        public IEnumerable<TrakHoundFunctionInformation> GetInformation(IEnumerable<string> functionIds)
        {
            if (!functionIds.IsNullOrEmpty())
            {
                var informations = new List<TrakHoundFunctionInformation>();

                foreach (var functionId in functionIds)
                {
                    var information = GetInformation(functionId);
                    if (information != null) informations.Add(information);
                }

                return informations;
            }

            return null;
        }

        #endregion

        #region "Metrics"

        /// <summary>
        /// Gets the Run Metrics for each Function that has been Run
        /// </summary>
        public IEnumerable<TrakHoundFunctionMetrics> GetMetrics() => _metrics.GetMetrics();

        /// <summary>
        /// Gets the Run Metrics, including the Recent Runs, for the specified Function (null if it has not been Run)
        /// </summary>
        public TrakHoundFunctionMetrics GetMetrics(string functionId) => _metrics.GetMetrics(functionId);

        #endregion


        public async Task<TrakHoundFunctionResponse> Run(string functionId, IReadOnlyDictionary<string, string> parameters = null, string runId = null, long timestamp = 0)
        {
            if (!string.IsNullOrEmpty(functionId))
            {
                var functionKey = functionId.ToLower();

                var functionItem = _functionsSnapshot.GetValueOrDefault(functionKey);
                if (functionItem != null)
                {
                    return await RunConfiguration(functionItem.Module, functionItem.Configuration, parameters, runId, timestamp);
                }
            }

            return new TrakHoundFunctionResponse();
        }


        public void AddConfiguration(ITrakHoundFunctionConfiguration configuration)
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Id))
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
                        LoadConfiguration(module, configuration);
                    }
                }
            }
        }

        /// <summary>
        /// Stops serving the Function loaded from the Configuration (called when the Configuration is removed). Runs already started are not canceled
        /// </summary>
        public void RemoveConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                lock (_lock)
                {
                    var functionId = _configurationFunctions.GetValueOrDefault(configurationId);
                    if (functionId != null)
                    {
                        // Another Configuration may have loaded a Function with the same ID since (it is kept)
                        var functionKey = functionId.ToLower();
                        if (_functions.TryGetValue(functionKey, out var functionItem) && functionItem.Configuration?.Id == configurationId)
                        {
                            _functions.Remove(functionKey);
                            _functionsSnapshot = new Dictionary<string, FunctionItem>(_functions);
                        }
                    }

                    _configurationFunctions.Remove(configurationId);
                    _installedConfigurations.Remove(configurationId);
                    _installedModuleHashes.Remove(configurationId);
                }
            }
        }


        private void LoadDelayElapsed(object sender, EventArgs args)
        {
            Load();
        }

        public void Load()
        {
            var configurations = _configurationProfile.Get<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations.ToList())
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
                                LoadConfiguration(module, configuration);
                            }
                        }
                    }
                }
            }
        }

        private void LoadConfiguration(ITrakHoundModule module, ITrakHoundFunctionConfiguration configuration)
        {
            if (module != null && configuration != null && configuration.Id != null && !string.IsNullOrEmpty(configuration.FunctionId))
            {
                // Read before the Client's Router, so a Group change in between reloads the Function again (instead of being missed)
                var loadHash = GetLoadHash(configuration);

                // Get Client
                var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, configuration.RouterId, configuration.GroupId))
                    .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.Function, configuration.Id, configuration.Name, configuration.PackageId));

                // Get Volume
                var volumeId = configuration.VolumeId ?? configuration.Id;
                var volume = _volumeProvider.GetVolume(volumeId);

                var variables = _variableProvider.GetCollection(configuration.Id);

                // Create a new instance of the Function Type
                var function = CreateInstance(module, configuration);
                if (function != null)
                {
                    function.InstanceId = _instance.Id;
                    function.Package = module.Package;
                    function.SetClient(client);
                    function.SetVolume(volume);
                    function.SetVariables(variables);

                    var functionItem = new FunctionItem();
                    functionItem.Module = module;
                    functionItem.Configuration = configuration;
                    functionItem.VolumeId = volumeId;

                    var functionKey = configuration.FunctionId.ToLower();

                    lock (_lock)
                    {
                        _functions.Remove(functionKey);
                        _functions.Add(functionKey, functionItem);
                        _functionsSnapshot = new Dictionary<string, FunctionItem>(_functions);

                        _configurationFunctions.Remove(configuration.Id);
                        _configurationFunctions.Add(configuration.Id, configuration.FunctionId);

                        _installedConfigurations.Remove(configuration.Id);
                        _installedModuleHashes.Remove(configuration.Id);

                        // Add to installed List
                        _installedConfigurations.Add(configuration.Id, loadHash);
                        _installedModuleHashes.Add(configuration.Id, module.Package.Hash);
                    }
                }
            }
        }

        /// <summary>
        /// The Configuration's Hash and the Router it resolves to (so the Function reloads when its Group's Router changes)
        /// </summary>
        private string GetLoadHash(ITrakHoundFunctionConfiguration configuration)
        {
            return TrakHoundGroups.GetLoadHash(_configurationProfile, configuration.Hash, configuration.RouterId, configuration.GroupId);
        }


        /// <summary>
        /// The Parameters the Function type describes (FunctionParameter attributes), read once per type
        /// </summary>
        private static IReadOnlyList<TrakHoundFunctionParameterInformation> GetParameterDefinitions(Type functionType)
        {
            if (functionType == null) return null;
            return _parameterDefinitions.GetValue(functionType, type => TrakHoundFunctionParameterInformation.Create(type));
        }

        private async Task<TrakHoundFunctionResponse> RunConfiguration(ITrakHoundModule module, ITrakHoundFunctionConfiguration configuration, IReadOnlyDictionary<string, string> parameters, string runId, long timestamp)
        {
            if (module != null && configuration != null && configuration.Id != null && !string.IsNullOrEmpty(configuration.FunctionId))
            {
                // Get Client
                var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, configuration.RouterId, configuration.GroupId))
                    .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.Function, configuration.Id, configuration.Name, configuration.PackageId));

                // Get Volume
                var volumeId = configuration.VolumeId ?? configuration.Id;
                var volume = _volumeProvider.GetVolume(volumeId);

                var variables = _variableProvider.GetCollection(configuration.Id);

                var function = CreateInstance(module, configuration);
                if (function != null)
                {
                    function.InstanceId = _instance.Id;
                    function.Package = module.Package;
                    function.SetClient(client);
                    function.SetVolume(volume);
                    function.SetVariables(variables);

                    var cpuMonitor = CpuMonitoringEnabled ? TrakHoundModuleCpuMonitors.Default.GetMonitor(TrakHound.Instances.TrakHoundCpuModuleTypes.Function, configuration.FunctionId, !string.IsNullOrEmpty(configuration.Name) ? configuration.Name : module.PackageId) : null;
                    var parameterDefinitions = GetParameterDefinitions(module.ModuleTypes?.FirstOrDefault());
                    using (var engine = new TrakHoundFunctionEngine(function, parameters, module.PackageId, module.PackageVersion, runId, CpuMonitoringEnabled, cpuMonitor, parameterDefinitions))
                    {
                        engine.StatusReceived += FunctionStatusReceivedHandler;
                        engine.LogReceived += FunctionLogReceivedHandler;

                        if (engine.RunId != null)
                        {
                            lock (_lock)
                            {
                                _engines.Remove(engine.RunId);
                                _engines.Add(engine.RunId, engine);
                            }

                            if (EngineAdded != null) EngineAdded.Invoke(this, engine);

                            var recordMetrics = MetricsEnabled;
                            if (recordMetrics) _metrics.RunStarted(configuration.FunctionId, configuration.Name, module.PackageId, module.PackageVersion, engine.RunId, DateTime.UtcNow);

                            var statusCode = 500;
                            string message = null;

                            try
                            {
                                var response = await engine.Run(timestamp);
                                statusCode = response.StatusCode;
                                message = response.GetParameter("message");
                                return response;
                            }
                            finally
                            {
                                if (recordMetrics) _metrics.RunCompleted(configuration.FunctionId, engine.RunId, statusCode, message, DateTime.UtcNow, engine.TotalProcessorTime);

                                lock (_lock) _engines.Remove(engine.RunId);

                                // Engines are keyed by RunId (see EngineAdded / GetEngine)
                                if (EngineRemoved != null) EngineRemoved.Invoke(this, engine.RunId);
                            }
                        }
                    }
                }
            }

            return new TrakHoundFunctionResponse();
        }


        private void FunctionStatusReceivedHandler(ITrakHoundFunction function, string runId, TrakHoundStatusItem item)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                if (StatusUpdated != null) StatusUpdated.Invoke(runId, item.StatusType);
            }
        }

        private void FunctionLogReceivedHandler(ITrakHoundFunction function, string runId, TrakHoundLogItem item)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                if (LogUpdated != null) LogUpdated.Invoke(runId, item);
            }
        }

        private static TrakHoundFunction CreateInstance(ITrakHoundModule module, ITrakHoundFunctionConfiguration configuration)
        {
            if (module != null && module.ModuleTypes != null && configuration != null)
            {
                try
                {
                    var constructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] {
                         typeof(ITrakHoundFunctionConfiguration)
                         });
                    if (constructor != null)
                    {
                        return (TrakHoundFunction)constructor.Invoke(new object[] { configuration });
                    }
                    else
                    {
                        // Should only be needed temporarily. 6/15/2026
                        var backwardsCompatibleConstructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] {
                            typeof(ITrakHoundFunctionConfiguration), 
                            typeof(ITrakHoundClient),
                            typeof(ITrakHoundVolume)
                         });
                        if (backwardsCompatibleConstructor != null)
                        {
                            ITrakHoundClient client = null;
                            ITrakHoundVolume volume = null;

                            return (TrakHoundFunction)backwardsCompatibleConstructor.Invoke(new object[] { configuration, client, volume });
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
