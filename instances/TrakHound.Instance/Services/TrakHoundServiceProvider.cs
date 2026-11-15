// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using TrakHound.Configurations;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Services.Blobs;
using TrakHound.Services.KeyValues;
using TrakHound.Services.TimeSeries;
using TrakHound.Services.MessageQueues;
using TrakHound.Services.Messages;
using TrakHound.Services.Tables;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Services
{
    /// <summary>
    /// Loads Services (ex. Table, Key Value, Blob, Message drivers) from Service Packages based on the Service Configurations
    /// </summary>
    public class TrakHoundServiceProvider : ITrakHoundServiceProvider
    {
        private const string _packageCategory = "service";

        // Replaced Services are Disposed after this delay, so that Routers (which hold references to Service instances)
        // have time to remap to the new instances first (the Router Provider reloads 1 second after a Service is added)
        private static readonly TimeSpan _replacedServiceDisposeDelay = TimeSpan.FromSeconds(10);

        private readonly string _instanceId;
        private readonly Dictionary<string, TrakHoundServiceInformation> _serviceInformations = new Dictionary<string, TrakHoundServiceInformation>();
        private readonly ListDictionary<string, ITrakHoundService> _services = new ListDictionary<string, ITrakHoundService>();
        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly ITrakHoundLogger _logger;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly object _lock = new object();
        private readonly object _loadLock = new object();
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(5000);
        private bool _disposed;

        private readonly Dictionary<string, string> _installedConfigurations = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(); // Configuration.Id => Module.Hash


        public IEnumerable<ITrakHoundService> Services => _services.Values;

        public EventHandler<ITrakHoundService> ServiceAdded { get; set; }

        public EventHandler<string> ServiceRemoved { get; set; }

        public EventHandler<Exception> ServiceLoadError { get; set; }


        public TrakHoundServiceProvider(
            string instanceId,
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            TrakHoundPackageManager packageManager
            )
        {
            _instanceId = instanceId;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;
            _logger = logProvider?.GetLoggerByName("services");

            if (configurationProfile != null)
            {
                _configurationProfile = configurationProfile;
                _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
                _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;
            }

            if (packageManager != null)
            {
                _packageManager = packageManager;
                _packageManager.PackageAdded += PackageAdded;
                _packageManager.PackageRemoved += PackageRemoved;
            }

            if (moduleProvider != null)
            {
                _moduleManager = moduleProvider.Get<ITrakHoundService>(_packageCategory);
            }

            _delayLoadEvent.Elapsed += LoadDelayElapsed;
        }

        public void Dispose()
        {
            lock (_loadLock)
            {
                if (_disposed) return;
                _disposed = true;

                _delayLoadEvent.Elapsed -= LoadDelayElapsed;
                _delayLoadEvent.Dispose();

                if (_configurationProfile != null)
                {
                    _configurationProfile.ConfigurationAdded -= ConfigurationUpdated;
                    _configurationProfile.ConfigurationRemoved -= ConfigurationUpdated;
                }

                if (_packageManager != null)
                {
                    _packageManager.PackageAdded -= PackageAdded;
                    _packageManager.PackageRemoved -= PackageRemoved;
                }

                // Dispose of all of the Services
                DisposeServices(_services.Values);
                _services.Clear();

                lock (_lock)
                {
                    _serviceInformations.Clear();
                    _installedConfigurations.Clear();
                    _installedModuleHashes.Clear();
                }
            }
        }


        public IEnumerable<TrakHoundServiceInformation> GetInformation()
        {
            lock (_lock) return _serviceInformations.Values.ToList();
        }

        public TrakHoundServiceInformation GetInformation(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                lock (_lock) return _serviceInformations.GetValueOrDefault(configurationId);
            }

            return null;
        }


        private void ConfigurationUpdated(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration != null && configuration.Category == TrakHoundServiceConfiguration.ConfigurationCategory)
            {
                _delayLoadEvent.Set();
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

        private void LoadDelayElapsed(object sender, EventArgs args)
        {
            // Exceptions in Timer callbacks are swallowed, so log them here
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Service Load Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        public void Load()
        {
            // Only one Load at a time
            lock (_loadLock)
            {
                if (_disposed || _configurationProfile == null || _moduleManager == null) return;

                var foundConfigurationIds = new HashSet<string>();

                var configurations = _configurationProfile.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory);
                if (!configurations.IsNullOrEmpty())
                {
                    foreach (var configuration in configurations.ToList())
                    {
                        if (string.IsNullOrEmpty(configuration.PackageId) || string.IsNullOrEmpty(configuration.PackageVersion)) continue;

                        var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                        if (module != null && !module.ModuleTypes.IsNullOrEmpty())
                        {
                            foundConfigurationIds.Add(configuration.Id);

                            // Get the Installed Hash (to check if the configuration has changed)
                            string installedHash;
                            string installedModuleHash;
                            lock (_lock)
                            {
                                installedHash = _installedConfigurations.GetValueOrDefault(configuration.Id);
                                installedModuleHash = _installedModuleHashes.GetValueOrDefault(configuration.Id);
                            }

                            if (configuration.Hash != installedHash || module.Package.Hash != installedModuleHash)
                            {
                                LoadConfiguration(configuration, module);
                            }
                        }
                        else
                        {
                            _logger?.LogError($"Service Load Error : Module Not Found : {configuration.Id} : Package ID = {configuration.PackageId} v{configuration.PackageVersion}");
                        }
                    }
                }

                // Remove unused Services
                foreach (var configurationId in _services.Keys)
                {
                    if (!foundConfigurationIds.Contains(configurationId))
                    {
                        RemoveService(configurationId);
                    }
                }
            }
        }

        private void LoadConfiguration(TrakHoundServiceConfiguration configuration, ITrakHoundModule module)
        {
            var services = new List<ITrakHoundService>();
            var routes = new List<string>();
            var failed = false;

            // Create the new Services
            foreach (var moduleType in module.ModuleTypes)
            {
                var service = CreateService(configuration, moduleType, out var serviceFailed);
                if (serviceFailed) failed = true;

                if (service != null)
                {
                    services.Add(service);

                    var route = GetServiceRoute(moduleType);
                    if (route != null) routes.Add(route);
                }
            }

            var previousServices = _services.Get(configuration.Id)?.ToList();

            // Keep the previous Services running if none of the new Services could be created (retried on the next Load)
            if (services.Count < 1 && !previousServices.IsNullOrEmpty())
            {
                _logger?.LogError($"Service Load Error : {configuration.Id} : No Services could be created. The previous Services remain loaded.");
                return;
            }

            // Replace the previous Services (the new Services are added before the previous are removed)
            _services.Remove(configuration.Id);
            foreach (var service in services) _services.Add(configuration.Id, service);

            foreach (var service in services) ServiceAdded?.Invoke(this, service);

            DisposeServicesDelayed(previousServices);

            var information = new TrakHoundServiceInformation();
            information.ConfigurationId = configuration.Id;
            information.Name = configuration.Name;
            information.Description = configuration.Description;
            information.PackageId = module.PackageId;
            information.PackageVersion = module.PackageVersion;
            information.Routes = routes;

            lock (_lock)
            {
                _serviceInformations[configuration.Id] = information;

                if (!failed)
                {
                    _installedConfigurations[configuration.Id] = configuration.Hash;
                    _installedModuleHashes[configuration.Id] = module.Package.Hash;
                }
                else
                {
                    // Not marked as Installed so that the Configuration is retried on the next Load
                    _installedConfigurations.Remove(configuration.Id);
                    _installedModuleHashes.Remove(configuration.Id);
                }
            }
        }

        private static string GetServiceRoute(Type moduleType)
        {
            if (typeof(ITrakHoundTableService).IsAssignableFrom(moduleType)) return "Tables";
            if (typeof(ITrakHoundTimeSeriesService).IsAssignableFrom(moduleType)) return "TimeSeries";
            if (typeof(ITrakHoundKeyValueService).IsAssignableFrom(moduleType)) return "KeyValues";
            if (typeof(ITrakHoundBlobService).IsAssignableFrom(moduleType)) return "Blobs";
            if (typeof(ITrakHoundMessageService).IsAssignableFrom(moduleType)) return "Messages";
            if (typeof(ITrakHoundMessageQueueService).IsAssignableFrom(moduleType)) return "MessageQueues";
            return null;
        }

        private TrakHoundService CreateService(ITrakHoundServiceConfiguration configuration, Type type, out bool failed)
        {
            failed = false;

            if (configuration != null && !type.IsInterface && !type.IsAbstract)
            {
                try
                {
                    var constructor = type.GetConstructor(new Type[] { typeof(ITrakHoundServiceConfiguration) });
                    if (constructor != null)
                    {
                        var service = (TrakHoundService)constructor.Invoke(new object[] { configuration });
                        if (service != null)
                        {
                            service.InstanceId = _instanceId;
                            service.LogReceived += ServiceLogReceived;
                            service.LogLevelEnabled = IsServiceLogLevelEnabled;

                            var volumeId = configuration.VolumeId ?? configuration.Id;
                            if (_volumeProvider != null) service.SetVolume(_volumeProvider.GetVolume(volumeId));

                            if (_variableProvider != null) service.SetVariables(_variableProvider.GetCollection(configuration.Id));

                            return service;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    ServiceLoadError?.Invoke(this, ex);
                }
            }

            return null;
        }

        public void AddService(ITrakHoundServiceConfiguration configuration, Type type)
        {
            var service = CreateService(configuration, type, out _);
            if (service != null)
            {
                _services.Add(configuration.Id, service);
                ServiceAdded?.Invoke(this, service);
            }
        }

        public void AddService(ITrakHoundService service)
        {
            if (service?.Configuration != null)
            {
                if (service is TrakHoundService trakhoundService)
                {
                    trakhoundService.InstanceId = _instanceId;

                    var volumeId = service.Configuration.VolumeId ?? service.Configuration.Id;
                    if (_volumeProvider != null) trakhoundService.SetVolume(_volumeProvider.GetVolume(volumeId));

                    if (_variableProvider != null) trakhoundService.SetVariables(_variableProvider.GetCollection(service.Configuration.Id));

                    trakhoundService.LogReceived += ServiceLogReceived;
                    trakhoundService.LogLevelEnabled = IsServiceLogLevelEnabled;
                }

                _services.Add(service.Configuration.Id, service);

                ServiceAdded?.Invoke(this, service);
            }
        }

        public void RemoveService(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                var services = _services.Get(configurationId)?.ToList();
                _services.Remove(configurationId);

                lock (_lock)
                {
                    _serviceInformations.Remove(configurationId);

                    // Remove the Installed Hashes so that the Service is Loaded again if it is added back unchanged
                    _installedConfigurations.Remove(configurationId);
                    _installedModuleHashes.Remove(configurationId);
                }

                ServiceRemoved?.Invoke(this, configurationId);

                DisposeServicesDelayed(services);
            }
        }


        private void DisposeServicesDelayed(IEnumerable<ITrakHoundService> services)
        {
            if (!services.IsNullOrEmpty())
            {
                var servicesToDispose = services.ToList();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(_replacedServiceDisposeDelay);
                    DisposeServices(servicesToDispose);
                });
            }
        }

        private void DisposeServices(IEnumerable<ITrakHoundService> services)
        {
            if (!services.IsNullOrEmpty())
            {
                foreach (var service in services)
                {
                    try
                    {
                        if (service is TrakHoundService trakhoundService) trakhoundService.LogReceived -= ServiceLogReceived;
                        service.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Service Dispose Error : {service.Configuration?.Id} : {ex.Message}");
                    }
                }
            }
        }

        // Service Loggers have no subscribers of their own, so a Level is received when the Log Provider's Loggers raise it
        private bool IsServiceLogLevelEnabled(TrakHoundLogLevel level)
        {
            return _logProvider is not TrakHoundLogProviderBase logProvider || level <= logProvider.LoggerMinimumLevel;
        }

        private void ServiceLogReceived(object sender, TrakHoundLogItem logItem)
        {
            if (sender is ITrakHoundService service && logItem.Sender != null && _logProvider != null)
            {
                // Use the Configuration ID if the Service has no Name (an empty Name would log directly to the Package)
                var serviceName = !string.IsNullOrEmpty(service.Configuration.Name) ? service.Configuration.Name : service.Configuration.Id;

                var loggerName = $"services/{service.Configuration.PackageId}/{serviceName}";
                loggerName = TrakHoundPath.Combine(loggerName, logItem.Sender);

                var logger = _logProvider.GetLoggerByName(loggerName);
                logger.Log(logItem);
            }
        }

        public TService GetService<TService>(string configurationId) where TService : ITrakHoundService
        {
            // Get the Service that matches the specified ITrakHoundService (TService)
            if (!string.IsNullOrEmpty(configurationId))
            {
                var configurationServices = _services.Get(configurationId);
                if (!configurationServices.IsNullOrEmpty())
                {
                    return (TService)configurationServices.FirstOrDefault(x => typeof(TService).IsAssignableFrom(x.GetType()));
                }
            }

            return default;
        }
    }
}
