// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Apps
{
    /// <summary>
    /// Creates the Injection Services used by Apps.
    /// When an App is updated (hot loaded) the previous Version is Retired instead of being replaced, so Sessions (Browser connections)
    /// that are still running the previous Version keep using its Services. A Retired Version is removed (and its Services Disposed) once
    /// the last Session using it has ended.
    /// </summary>
    public class TrakHoundAppInjectionServiceManager : ITrakHoundAppInjectionServiceManager, IDisposable
    {
        private static readonly string _packageCategory = "app";

        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly Func<IServiceProvider> _getServiceProvider;
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(2000);
        private readonly object _lock = new object();
        private readonly object _loadLock = new object();

        // Services
        private readonly Dictionary<string, TrakHoundAppInjectionServiceConfiguration> _serviceConfigurations = new Dictionary<string, TrakHoundAppInjectionServiceConfiguration>(); // TypeKey => Service Configuration (used to create new Services)
        private readonly Dictionary<ServiceKey, object> _services = new Dictionary<ServiceKey, object>(); // ServiceKey => Service (object)
        private readonly ListDictionary<string, ServiceKey> _scopeServices = new ListDictionary<string, ServiceKey>(); // ScopeId => ServiceKey
        private readonly Dictionary<ServiceKey, string> _serviceScopes = new Dictionary<ServiceKey, string>(); // ServiceKey => ScopeId
        private readonly Dictionary<ServiceKey, string> _serviceTypeKeys = new Dictionary<ServiceKey, string>(); // ServiceKey => TypeKey

        // Versions (an App Configuration running a specific Package Version)
        private readonly Dictionary<string, AppVersion> _versions = new Dictionary<string, AppVersion>(); // VersionKey => Version
        private readonly Dictionary<string, string> _currentVersions = new Dictionary<string, string>(); // Configuration.Id => VersionKey
        private readonly Dictionary<string, string> _typeVersions = new Dictionary<string, string>(); // TypeKey => VersionKey (the Version the Type's Service Configuration belongs to)

        // Sessions (Browser connections) using each Version
        private readonly Dictionary<string, HashSet<string>> _versionSessions = new Dictionary<string, HashSet<string>>(); // VersionKey => SessionId
        private readonly Dictionary<string, HashSet<string>> _sessionVersions = new Dictionary<string, HashSet<string>>(); // SessionId => VersionKey

        private Dictionary<string, string> _installedConfigurations = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash and the Router it resolves to
        private Dictionary<string, string> _installedPackages = new Dictionary<string, string>(); // Configuration.Id => Package.Uuid


        class AppVersion
        {
            public string Key { get; set; }
            public string ConfigurationId { get; set; }
            public string PackageId { get; set; }
            public string PackageVersion { get; set; }
            public Dictionary<string, TrakHoundAppInjectionServiceConfiguration> Types { get; set; } = new Dictionary<string, TrakHoundAppInjectionServiceConfiguration>(); // TypeKey => Service Configuration
        }


        public IServiceProvider ServiceProvider => _getServiceProvider();


        public TrakHoundAppInjectionServiceManager(
            ITrakHoundConfigurationProfile configurationProfile,
            TrakHoundPackageManager packageManager,
            ITrakHoundModuleProvider moduleProvider,
            TrakHoundModuleContext context,
            ITrakHoundClientProvider clientProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            Func<IServiceProvider> getServiceProvider
            )
        {
            _configurationProfile = configurationProfile;
            _packageManager = packageManager;
            _moduleManager = moduleProvider.Get<ITrakHoundAppInjectionService>(_packageCategory, context, false);

            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;

            _getServiceProvider = getServiceProvider;

            Load();

            _delayLoadEvent.Elapsed += DelayElapsed;

            _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;
        }

        public void Dispose()
        {
            _delayLoadEvent.Elapsed -= DelayElapsed;
            _delayLoadEvent.Dispose();

            _configurationProfile.ConfigurationAdded -= ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved -= ConfigurationUpdated;

            _packageManager.PackageAdded -= PackageAdded;
            _packageManager.PackageRemoved -= PackageRemoved;

            lock (_lock)
            {
                _serviceConfigurations.Clear();
                _services.Clear();
                _scopeServices.Clear();
                _serviceScopes.Clear();
                _serviceTypeKeys.Clear();

                _versions.Clear();
                _currentVersions.Clear();
                _typeVersions.Clear();
                _versionSessions.Clear();
                _sessionVersions.Clear();

                _installedConfigurations.Clear();
                _installedPackages.Clear();
            }
        }


        private void ConfigurationUpdated(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration != null && configuration.Category == TrakHoundAppConfiguration.ConfigurationCategory)
            {
                _delayLoadEvent.Set();
            }
            else if (configuration != null && configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
            {
                // Reload the Apps whose Router came from the Group (unchanged Apps are skipped)
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


        #region "Services"

        public object GetService(string scopeId, Type serviceType)
        {
            if (string.IsNullOrEmpty(scopeId) || serviceType == null) return null;

            var typeKey = CreateTypeKey(serviceType);
            var serviceKey = new ServiceKey(scopeId, typeKey);

            object service;
            lock (_lock) service = _services.GetValueOrDefault(serviceKey);
            if (service != null) return service;

            TrakHoundAppInjectionServiceConfiguration serviceConfiguration;
            lock (_lock) serviceConfiguration = _serviceConfigurations.GetValueOrDefault(typeKey);

            if (serviceConfiguration != null && serviceConfiguration.Configuration != null)
            {
                service = CreateService(scopeId, serviceConfiguration);
                if (service != null)
                {
                    // Another thread may have created the same Service first (use the registered one)
                    var registeredService = AddService(scopeId, service);
                    if (!ReferenceEquals(registeredService, service))
                    {
                        DisposeObject(service);
                        return registeredService;
                    }

                    var logger = _logProvider.GetLoggerByName("app/services");
                    logger.LogDebug($"Injection Service : REGISTER : {serviceConfiguration.Configuration.Id} : {scopeId} : {service.GetType().Name}");
                }
            }

            return service;
        }

        public object GetService<TServiceType>(string scopeId, Type serviceType) where TServiceType : ITrakHoundAppInjectionService
        {
            return GetService(scopeId, serviceType);
        }


        public IEnumerable<TrakHoundAppInjectionServiceConfiguration> GetServiceConfigurations<TServiceType>() where TServiceType : ITrakHoundAppInjectionService
        {
            List<TrakHoundAppInjectionServiceConfiguration> allServices;
            lock (_lock) allServices = _serviceConfigurations.Values.ToList();

            return allServices.Where(o => typeof(TServiceType).IsAssignableFrom(o.ServiceType)).ToList();
        }

        public IEnumerable<TrakHoundAppInjectionServiceConfiguration> GetRequiredServiceConfigurations<TServiceType>() where TServiceType : ITrakHoundAppInjectionService
        {
            var result = new List<TrakHoundAppInjectionServiceConfiguration>();

            if (typeof(ITrakHoundTransientAppInjectionService).IsAssignableFrom(typeof(TServiceType)))
            {
                result.AddRange(GetServiceConfigurations<ITrakHoundTransientAppInjectionService>());
                result.AddRange(GetServiceConfigurations<ITrakHoundScopedAppInjectionService>());
                result.AddRange(GetServiceConfigurations<ITrakHoundSingletonAppInjectionService>());
            }
            else if (typeof(ITrakHoundScopedAppInjectionService).IsAssignableFrom(typeof(TServiceType)))
            {
                result.AddRange(GetServiceConfigurations<ITrakHoundScopedAppInjectionService>());
                result.AddRange(GetServiceConfigurations<ITrakHoundSingletonAppInjectionService>());
            }
            else if (typeof(ITrakHoundSingletonAppInjectionService).IsAssignableFrom(typeof(TServiceType)))
            {
                result.AddRange(GetServiceConfigurations<ITrakHoundSingletonAppInjectionService>());
            }

            return result;
        }


        public void RegisterService(string scopeId, object service)
        {
            AddService(scopeId, service);
        }

        public void RegisterService<TServiceType>(string scopeId, object service) where TServiceType : ITrakHoundAppInjectionService
        {
            AddService(scopeId, service);
        }

        /// <summary>
        /// Register the Service for the Scope and return the registered Service (which is an existing Service if one was already registered)
        /// </summary>
        private object AddService(string scopeId, object service)
        {
            if (string.IsNullOrEmpty(scopeId) || service == null) return service;

            var serviceKey = CreateServiceKey(scopeId, service.GetType());

            lock (_lock)
            {
                var existingService = _services.GetValueOrDefault(serviceKey);
                if (existingService != null) return existingService;

                _services.Add(serviceKey, service);
                _serviceScopes[serviceKey] = scopeId;
                _serviceTypeKeys[serviceKey] = serviceKey.TypeKey;
            }

            // Add to list of scope services
            _scopeServices.Add(scopeId, serviceKey);

            return service;
        }


        public void DisposeScope(string scopeId) => DisposeScope(scopeId, true);

        public void DisposeScope(string scopeId, bool ignoreSingleton)
        {
            if (string.IsNullOrEmpty(scopeId)) return;

            // The Scope may be a Session (Browser connection) that was using one or more App Versions
            var releasedVersionKeys = RemoveSession(scopeId);

            var serviceKeys = _scopeServices.Get(scopeId);
            if (!serviceKeys.IsNullOrEmpty())
            {
                foreach (var serviceKey in serviceKeys)
                {
                    object service;
                    lock (_lock) service = _services.GetValueOrDefault(serviceKey);

                    if (service == null)
                    {
                        _scopeServices.Remove(scopeId, serviceKey);
                    }
                    else if (!ignoreSingleton || !(service is ITrakHoundSingletonAppInjectionService)) // Don't Dispose Singleton
                    {
                        DisposeService(serviceKey);
                    }
                }
            }

            // Remove any Retired Versions that were only being kept for this Session
            foreach (var versionKey in releasedVersionKeys)
            {
                TryRemoveVersion(versionKey);
            }
        }

        private void DisposeService(ServiceKey serviceKey)
        {
            if (string.IsNullOrEmpty(serviceKey.ScopeId) || serviceKey.TypeKey == null) return;

            object service;
            string scopeId;
            lock (_lock)
            {
                if (!_services.Remove(serviceKey, out service)) return;
                _serviceScopes.Remove(serviceKey, out scopeId);
                _serviceTypeKeys.Remove(serviceKey);
            }

            if (scopeId != null) _scopeServices.Remove(scopeId, serviceKey);

            DisposeObject(service);
        }

        private static void DisposeObject(object service)
        {
            try
            {
                if (service is IDisposable disposable) disposable.Dispose();
            }
            catch { }
        }


        public void ReloadServices(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return;

            var appConfiguration = _configurationProfile.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory, configurationId);
            if (appConfiguration != null)
            {
                // Dispose the Services for the Current Version (Retired Versions are left for the Sessions that are still using them)
                List<ServiceKey> serviceKeys;
                lock (_lock)
                {
                    var versionKey = _currentVersions.GetValueOrDefault(appConfiguration.Id);
                    var version = versionKey != null ? _versions.GetValueOrDefault(versionKey) : null;

                    serviceKeys = version != null
                        ? _serviceTypeKeys.Where(o => version.Types.ContainsKey(o.Value)).Select(o => o.Key).ToList()
                        : new List<ServiceKey>();
                }

                foreach (var serviceKey in serviceKeys)
                {
                    DisposeService(serviceKey);
                }

                LoadConfiguration(appConfiguration);
            }
        }


        private object CreateService(string scopeId, TrakHoundAppInjectionServiceConfiguration serviceConfiguration)
        {
            if (!string.IsNullOrEmpty(scopeId) && serviceConfiguration != null)
            {
                try
                {
                    var serviceProvider = new TrakHoundServiceProvider(scopeId, this);

                    // Add App Configuration
                    serviceProvider.AddService<ITrakHoundAppConfiguration>(serviceConfiguration.Configuration);

                    // Allow injection of ITrakHoundClient for the specified RouterId set in the AppConfiguration
                    serviceProvider.AddService<ITrakHoundClient>(serviceConfiguration.Client);

                    // Allow injection of ITrakHoundVolume
                    serviceProvider.AddService<ITrakHoundVolume>(serviceConfiguration.Volume);

                    // Allow injection of ITrakHoundVariableCollection
                    serviceProvider.AddService<ITrakHoundVariableCollection>(serviceConfiguration.Variables);

                    var service = ActivatorUtilities.CreateInstance(serviceProvider, serviceConfiguration.ServiceType);
                    serviceProvider.AddService(service);
                    return service;
                }
                catch (Exception ex)
                {
                    var logger = _logProvider.GetLoggerByName("app/services");
                    logger.LogError($"Injection Service : CreateService : {serviceConfiguration.Configuration.Id} : {scopeId} : {serviceConfiguration.ServiceType.Name} : {ex.HResult} : {ex.Message} : {ex.StackTrace}");
                }
            }

            return null;
        }

        #endregion

        #region "Sessions"

        /// <summary>
        /// Record that the Session (Browser connection) is using the App Configuration's Package Version (so it isn't removed while the Session is using it)
        /// </summary>
        public void AddSession(string sessionId, string configurationId, string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(configurationId) || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(packageVersion)) return;

            var versionKey = CreateVersionKey(configurationId, TrakHoundPackage.GenerateUuid(packageId, packageVersion));

            lock (_lock)
            {
                GetOrAddVersion(versionKey, configurationId, packageId, packageVersion);
                AddSessionVersion(sessionId, versionKey);
            }
        }

        /// <summary>
        /// Record that the Session (Browser connection) is using the Version that the Service Type belongs to
        /// </summary>
        public void AddServiceSession(string sessionId, Type serviceType)
        {
            if (string.IsNullOrEmpty(sessionId) || serviceType == null) return;

            var typeKey = CreateTypeKey(serviceType);

            lock (_lock)
            {
                var versionKey = _typeVersions.GetValueOrDefault(typeKey);
                if (versionKey != null) AddSessionVersion(sessionId, versionKey);
            }
        }

        /// <summary>
        /// Get the App Versions that are being used by Sessions (Browser connections), including Retired Versions
        /// (compare with the App Provider's loaded Version to tell if a Version is Retired)
        /// </summary>
        public IEnumerable<TrakHoundAppVersionSessions> GetVersionSessions()
        {
            lock (_lock)
            {
                var result = new List<TrakHoundAppVersionSessions>();
                foreach (var versionSessions in _versionSessions)
                {
                    var version = _versions.GetValueOrDefault(versionSessions.Key);
                    if (version != null)
                    {
                        result.Add(new TrakHoundAppVersionSessions
                        {
                            ConfigurationId = version.ConfigurationId,
                            PackageId = version.PackageId,
                            PackageVersion = version.PackageVersion,
                            SessionCount = versionSessions.Value.Count
                        });
                    }
                }
                return result;
            }
        }

        // Call within _lock
        private void AddSessionVersion(string sessionId, string versionKey)
        {
            if (!_versionSessions.TryGetValue(versionKey, out var sessionIds))
            {
                sessionIds = new HashSet<string>();
                _versionSessions.Add(versionKey, sessionIds);
            }
            sessionIds.Add(sessionId);

            if (!_sessionVersions.TryGetValue(sessionId, out var versionKeys))
            {
                versionKeys = new HashSet<string>();
                _sessionVersions.Add(sessionId, versionKeys);
            }
            versionKeys.Add(versionKey);
        }

        /// <summary>
        /// Remove the Session and return the Versions that no longer have any Sessions
        /// </summary>
        private List<string> RemoveSession(string sessionId)
        {
            var releasedVersionKeys = new List<string>();

            lock (_lock)
            {
                if (_sessionVersions.Remove(sessionId, out var versionKeys))
                {
                    foreach (var versionKey in versionKeys)
                    {
                        if (_versionSessions.TryGetValue(versionKey, out var sessionIds))
                        {
                            sessionIds.Remove(sessionId);
                            if (sessionIds.Count == 0)
                            {
                                _versionSessions.Remove(versionKey);
                                releasedVersionKeys.Add(versionKey);
                            }
                        }
                    }
                }
            }

            return releasedVersionKeys;
        }

        #endregion

        #region "Versions"

        // Call within _lock
        private AppVersion GetOrAddVersion(string versionKey, string configurationId, string packageId, string packageVersion)
        {
            if (!_versions.TryGetValue(versionKey, out var version))
            {
                version = new AppVersion
                {
                    Key = versionKey,
                    ConfigurationId = configurationId,
                    PackageId = packageId,
                    PackageVersion = packageVersion
                };
                _versions.Add(versionKey, version);
            }

            return version;
        }

        /// <summary>
        /// Remove the Version (and Dispose its Services) if it is not the Current Version and is no longer used by any Sessions
        /// </summary>
        private void TryRemoveVersion(string versionKey)
        {
            if (string.IsNullOrEmpty(versionKey)) return;

            AppVersion version;
            var disposeServiceKeys = new List<ServiceKey>();

            lock (_lock)
            {
                if (!_versions.TryGetValue(versionKey, out version)) return;
                if (_currentVersions.GetValueOrDefault(version.ConfigurationId) == versionKey) return;
                if (_versionSessions.ContainsKey(versionKey)) return;

                _versions.Remove(versionKey);

                foreach (var typeKey in version.Types.Keys)
                {
                    // Another Version (ex. another App Configuration using the same Package Version) may still use the Type
                    var otherVersion = _versions.Values.FirstOrDefault(o => o.Types.ContainsKey(typeKey));
                    if (otherVersion != null)
                    {
                        if (_typeVersions.GetValueOrDefault(typeKey) == versionKey)
                        {
                            _typeVersions[typeKey] = otherVersion.Key;
                            _serviceConfigurations[typeKey] = otherVersion.Types[typeKey];
                        }
                    }
                    else
                    {
                        _typeVersions.Remove(typeKey);
                        _serviceConfigurations.Remove(typeKey);
                        disposeServiceKeys.AddRange(_serviceTypeKeys.Where(o => o.Value == typeKey).Select(o => o.Key));
                    }
                }
            }

            foreach (var serviceKey in disposeServiceKeys)
            {
                DisposeService(serviceKey);
            }

            if (version.Types.Count > 0)
            {
                var logger = _logProvider.GetLoggerByName("app/services");
                logger.LogInformation($"Injection Service : REMOVED : {version.ConfigurationId} : {version.PackageId} v{version.PackageVersion} : {disposeServiceKeys.Count} Service(s) Disposed");
            }
        }

        /// <summary>
        /// Retire the Version (it is removed once it is no longer used by any Sessions)
        /// </summary>
        private void RetireVersion(string versionKey)
        {
            AppVersion version;
            int sessionCount;
            lock (_lock)
            {
                version = _versions.GetValueOrDefault(versionKey);
                sessionCount = _versionSessions.GetValueOrDefault(versionKey)?.Count ?? 0;
            }

            if (version != null && sessionCount > 0 && version.Types.Count > 0)
            {
                var logger = _logProvider.GetLoggerByName("app/services");
                logger.LogInformation($"Injection Service : RETIRED : {version.ConfigurationId} : {version.PackageId} v{version.PackageVersion} : Kept for {sessionCount} Session(s)");
            }

            TryRemoveVersion(versionKey);
        }

        #endregion

        #region "Load"

        private void DelayElapsed(object sender, EventArgs e)
        {
            Load();
        }

        private void Load()
        {
            var appConfigurations = _configurationProfile.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory)?.ToList() ?? new List<TrakHoundAppConfiguration>();

            foreach (var appConfiguration in appConfigurations)
            {
                LoadConfiguration(appConfiguration);
            }

            // Retire the Versions of App Configurations that were removed
            var configurationIds = new HashSet<string>(appConfigurations.Select(o => o.Id));
            List<KeyValuePair<string, string>> removedConfigurations;
            lock (_lock)
            {
                removedConfigurations = _currentVersions.Where(o => !configurationIds.Contains(o.Key)).ToList();
                foreach (var removedConfiguration in removedConfigurations)
                {
                    _currentVersions.Remove(removedConfiguration.Key);
                    _installedConfigurations.Remove(removedConfiguration.Key);
                    _installedPackages.Remove(removedConfiguration.Key);
                }
            }

            foreach (var removedConfiguration in removedConfigurations)
            {
                RetireVersion(removedConfiguration.Value);
            }
        }

        private void LoadConfiguration(TrakHoundAppConfiguration appConfiguration)
        {
            if (appConfiguration == null || string.IsNullOrEmpty(appConfiguration.Route)) return;

            // Don't Load the same Configuration on multiple threads at the same time
            lock (_loadLock)
            {
                var module = _moduleManager.Get(appConfiguration.PackageId, appConfiguration.PackageVersion);
                if (module == null) return;

                var packageUuid = TrakHoundPackage.GenerateUuid(module.PackageId, module.PackageVersion);

                // With the Router it resolves to, so a change to its Group's Router reloads it (read before the Client's Router, so a change in between isn't missed)
                var loadHash = TrakHoundGroups.GetLoadHash(_configurationProfile, appConfiguration.Hash, appConfiguration.RouterId, appConfiguration.GroupId);

                // Get the Installed Hash (to check if the configuration has changed)
                string installedConfigurationHash;
                string installedPackageUuid;
                lock (_lock)
                {
                    installedConfigurationHash = _installedConfigurations.GetValueOrDefault(appConfiguration.Id);
                    installedPackageUuid = _installedPackages.GetValueOrDefault(appConfiguration.Id);
                }

                if (loadHash != installedConfigurationHash || packageUuid != installedPackageUuid)
                {
                    LoadModule(appConfiguration, module);

                    lock (_lock)
                    {
                        // Add to installed List
                        _installedConfigurations[appConfiguration.Id] = loadHash;
                        _installedPackages[appConfiguration.Id] = packageUuid;
                    }
                }
            }
        }

        private void LoadModule(ITrakHoundAppConfiguration configuration, ITrakHoundModule module)
        {
            if (configuration == null || configuration.Id == null || module == null || module.ModuleTypes.IsNullOrEmpty()) return;

            // Get Client
            var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, configuration.RouterId, configuration.GroupId))
                .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.App, configuration.Id, configuration.Name, configuration.PackageId));

            // Get Volume
            var volumeId = configuration.VolumeId ?? configuration.Id;
            var volume = _volumeProvider.GetVolume(volumeId);

            // Get Variables
            var variables = _variableProvider.GetCollection(configuration.Id);

            var types = new Dictionary<string, TrakHoundAppInjectionServiceConfiguration>();
            foreach (var moduleType in module.ModuleTypes)
            {
                types[CreateTypeKey(moduleType)] = new TrakHoundAppInjectionServiceConfiguration(configuration, client, volume, variables, moduleType);
            }

            var versionKey = CreateVersionKey(configuration.Id, TrakHoundPackage.GenerateUuid(module.PackageId, module.PackageVersion));
            string previousVersionKey;

            lock (_lock)
            {
                var version = GetOrAddVersion(versionKey, configuration.Id, module.PackageId, module.PackageVersion);
                version.Types = types;

                foreach (var type in types)
                {
                    _serviceConfigurations[type.Key] = type.Value;
                    _typeVersions[type.Key] = versionKey;
                }

                previousVersionKey = _currentVersions.GetValueOrDefault(configuration.Id);
                _currentVersions[configuration.Id] = versionKey;
            }

            // Retire the previous Version (Sessions that are still using it keep using its Services)
            if (previousVersionKey != null && previousVersionKey != versionKey)
            {
                RetireVersion(previousVersionKey);
            }
        }

        #endregion


        private static string CreateVersionKey(string configurationId, string packageUuid)
        {
            return $"{configurationId}:{packageUuid}";
        }

        // A Scope + Type pair (instead of an MD5 hash of both) so a lookup doesn't build or hash strings
        private readonly record struct ServiceKey(string ScopeId, string TypeKey);

        // Weak so a cached Type doesn't keep an unloaded (hot loaded) Package's assembly alive
        private static readonly ConditionalWeakTable<Type, string> _typeKeys = new ConditionalWeakTable<Type, string>();

        private static ServiceKey CreateServiceKey(string scopeId, Type serviceType)
        {
            return new ServiceKey(scopeId, CreateTypeKey(serviceType));
        }

        private static string CreateTypeKey(Type serviceType)
        {
            if (serviceType != null && serviceType.Assembly != null)
            {
                return _typeKeys.GetValue(serviceType, static type => $"{type.Assembly.Location}:{type.FullName}".ToMD5Hash());
            }

            return null;
        }
    }
}
