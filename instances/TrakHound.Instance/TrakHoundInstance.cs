// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Blazor.Apps;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Extensions;
using TrakHound.Functions;
using TrakHound.Instance.Api;
using TrakHound.Instance.Boots;
using TrakHound.Licenses;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Variables;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Instances
{
    /// <summary>
    /// A TrakHound Instance. Components are created in two groups :
    /// Core components (Configuration Profile, Packages, Modules, Clients, Security, Volumes, Variables) are created once by Initialize and live for the lifetime of the Instance.
    /// Runtime components (Services, Routers, APIs, Apps, Functions, Workers) are created, loaded, and disposed by Start and Stop.
    /// </summary>
    public sealed class TrakHoundInstance : ITrakHoundInstance
    {
        private const string _configurationPackageCategory = "configuration";

        private readonly string _configurationProfileId = TrakHoundConfigurationProfile.Default;
        private readonly string _configurationPath;
        private readonly string _packagesPath;
        private readonly string _volumesPath;
        private readonly string _variablesPath;

        private readonly string _version;
        private readonly DateTime _createdTime = DateTime.UtcNow;
        private DateTime _lastUpdated = DateTime.UtcNow;
        private DateTime? _startTime;
        private DateTime? _stopTime;
        private TrakHoundInstanceStatus _status;
        private bool _initialized;
        private bool _started;
        private bool _runtimeCreated;

        // Only one Initialize, Start, or Stop at a time
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

        // Only one Package Revert at a time (so the same Version isn't checked and removed twice)
        private readonly object _packageRevertLock = new object();

        private TrakHoundInstanceConfiguration _configuration;
        private TrakHoundInstanceInformation _instanceInformation;
        private TrakHoundConfigurationProfile _configurationProfile;
        private TrakHoundPackageManager _packageManager;
        private TrakHoundModuleProvider _moduleProvider;
        private TrakHoundSecurityManager _securityManager;
        private Services.TrakHoundServiceProvider _serviceProvider;
        private TrakHoundServiceRouterProvider _serviceRouterProvider;
        private TrakHoundServicesProvider _servicesProvider;
        private TrakHoundAppProvider _appProvider;
        private TrakHoundApiProvider _apiProvider;
        private TrakHoundFunctionManager _functionManager;
        private TrakHoundWorkerManager _workerManager;
        private TrakHoundVolumeProvider _volumeProvider;
        private TrakHoundVariableProvider _variableProvider;
        private TrakHoundInstanceClientProvider _clientProvider;
        private TrakHoundApiConnectionManager _apiConnectionManager;
        private TrakHound.Zones.TrakHoundZoneManager _zoneManager;
        private TrakHound.Instance.Audit.TrakHoundAuditMonitor _auditMonitor;
        private TrakHoundModuleTokenManager _moduleTokens;
        private TrakHoundInstanceMetricsConfiguration _metrics = new TrakHoundInstanceMetricsConfiguration();
        private readonly TrakHound.Instance.Health.TrakHoundHealthMonitor _health;
        private TrakHoundApiStreamManager _streamManager;
        private readonly TrakHound.Http.TrakHoundStreamMetricsCollector _streamMetrics = new TrakHound.Http.TrakHoundStreamMetricsCollector();
        private readonly TrakHound.Instance.Mcp.TrakHoundMcpMetricsCollector _mcpMetrics = new TrakHound.Instance.Mcp.TrakHoundMcpMetricsCollector();
        private readonly TrakHound.Http.TrakHoundHttpMetricsCollector _httpMetrics = new TrakHound.Http.TrakHoundHttpMetricsCollector();
        private readonly TrakHound.Http.TrakHoundHttpRequestHistory _httpRequests = new TrakHound.Http.TrakHoundHttpRequestHistory();
        private readonly ITrakHoundLogProvider _logProvider;


        public string Id => _configuration?.InstanceId;

        public string Version => _version;
        public string ConfigurationPath => _configurationPath;
        public string PackagesPath => _packagesPath;
        public string VolumesPath => _volumeProvider?.BasePath ?? _volumesPath; // The Provider applies the default
        public string VariablesPath => (_variableProvider as TrakHoundVariableProvider)?.BasePath ?? _variablesPath; // The Provider applies the default

        public TrakHoundInstanceStatus Status => _status;

        public DateTime CreatedTime => _createdTime;

        public DateTime LastUpdated => _lastUpdated;

        public DateTime? StartTime => _startTime;

        public DateTime? StopTime => _stopTime;


        public TrakHoundInstanceConfiguration Configuration => _configuration;
        public TrakHoundInstanceMetricsConfiguration Metrics => _metrics;
        public TrakHound.Instance.Health.TrakHoundHealthMonitor Health => _health;
        public ITrakHoundConfigurationProfile ConfigurationProfile => _configurationProfile;
        public TrakHoundInstanceInformation Information => _instanceInformation;
        public TrakHoundPackageManager PackageManager => _packageManager;

        /// <summary>
        /// Publishers trusted in addition to the Configuration Profile's Trusted Publishers (ex. synced from a TrakHound Package Manager). A key the Profile trusts keeps the Profile's settings
        /// </summary>
        public Func<IEnumerable<TrakHoundTrustedPublisher>> AdditionalTrustedPublishersProvider { get; set; }

        /// <summary>
        /// The attestation lists of the TrakHound Package Managers, for the RequireApproval Verification Mode (every Package is refused under it if null)
        /// </summary>
        public Func<TrakHoundPackageApprovals> PackageApprovalsProvider { get; set; }

        /// <summary>
        /// Reads the attestation lists again before a Package is installed under the RequireApproval Verification Mode
        /// </summary>
        public Func<CancellationToken, Task> PackageApprovalsRefresh { get; set; }
        public ITrakHoundModuleProvider ModuleProvider => _moduleProvider;
        public ITrakHoundSecurityManager SecurityManager => _securityManager;
        public TrakHoundServicesProvider ServicesProvider => _servicesProvider;
        public ITrakHoundAppProvider AppProvider => _appProvider;
        public TrakHoundApiProvider ApiProvider => _apiProvider;
        public TrakHound.Zones.TrakHoundZoneManager ZoneManager => _zoneManager;
        public TrakHound.Instance.Audit.TrakHoundAuditMonitor AuditMonitor => _auditMonitor;
        public TrakHoundModuleTokenManager ModuleTokens => _moduleTokens;
        public TrakHoundFunctionManager FunctionManager => _functionManager;
        public TrakHoundWorkerManager WorkerManager => _workerManager;
        public ITrakHoundVolumeProvider VolumeProvider => _volumeProvider;
        public ITrakHoundVariableProvider VariableProvider => _variableProvider;
        public ITrakHoundClientProvider ClientProvider => _clientProvider;
        public ITrakHoundLogProvider LogProvider => _logProvider;
        public TrakHoundApiConnectionManager ApiConnectionManager => _apiConnectionManager;
        public TrakHoundApiStreamManager StreamManager => _streamManager;
        public TrakHound.Http.TrakHoundStreamMetricsCollector StreamMetrics => _streamMetrics;
        public TrakHound.Instance.Mcp.TrakHoundMcpMetricsCollector McpMetrics => _mcpMetrics;
        public TrakHound.Http.TrakHoundHttpMetricsCollector HttpMetrics => _httpMetrics;
        public TrakHound.Http.TrakHoundHttpRequestHistory HttpRequests => _httpRequests;


        public IServiceProvider ServiceProvider { get; set; }


        public event EventHandler Starting;

        public event EventHandler Started;

        public event EventHandler Stopping;

        public event EventHandler Stopped;

        /// <summary>
        /// Raised when a Start finishes (Started or failed), with how long each step took
        /// </summary>
        public event EventHandler<TrakHoundInstanceRuntimeStart> RuntimeStartCompleted;

        public event TrakHoundInstanceStatusHandler StatusUpdated;

        public event TrakHoundInstanceLogHandler LogUpdated;


        public TrakHoundInstance(ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(null, null, null, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        public TrakHoundInstance(TrakHoundConfigurationProfile configurationProfile, ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(null, configurationProfile, null, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        public TrakHoundInstance(string configurationProfileId, ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(null, null, configurationProfileId, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        public TrakHoundInstance(TrakHoundInstanceConfiguration configuration, ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(configuration, null, null, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        public TrakHoundInstance(TrakHoundInstanceConfiguration configuration, TrakHoundConfigurationProfile configurationProfile, ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(configuration, configurationProfile, null, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        public TrakHoundInstance(TrakHoundInstanceConfiguration configuration, string configurationProfileId, ITrakHoundLogProvider logProvider, string configurationPath = null, string packagesPath = null, string volumesPath = null, string variablesPath = null)
            : this(configuration, null, configurationProfileId, logProvider, configurationPath, packagesPath, volumesPath, variablesPath) { }

        private TrakHoundInstance(
            TrakHoundInstanceConfiguration configuration,
            TrakHoundConfigurationProfile configurationProfile,
            string configurationProfileId,
            ITrakHoundLogProvider logProvider,
            string configurationPath,
            string packagesPath,
            string volumesPath,
            string variablesPath
            )
        {
            _version = GetInstanceVersion();
            _configuration = configuration;
            _configurationProfile = configurationProfile;
            if (!string.IsNullOrEmpty(configurationProfileId)) _configurationProfileId = configurationProfileId;
            _logProvider = logProvider;
            if (logProvider != null) logProvider.LogEntryReceived += LogReceived;
            _health = new TrakHound.Instance.Health.TrakHoundHealthMonitor(this, logProvider, GetHealthDiskPaths);
            _configurationPath = configurationPath;
            _packagesPath = packagesPath;
            _volumesPath = volumesPath;
            _variablesPath = variablesPath;
        }


        private static string GetInstanceVersion()
        {
            return FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
        }

        private ITrakHoundLogger GetLogger() => _logProvider?.GetLoggerByName("instance");

        // The directories the Instance writes to (for the Health Disk Check)
        private IEnumerable<(string Name, string Path)> GetHealthDiskPaths()
        {
            return new[]
            {
                ("Instance", AppContext.BaseDirectory),
                ("Configuration", _configurationPath),
                ("Packages", _packagesPath),
                ("Volumes", _volumesPath),
                ("Variables", _variablesPath)
            };
        }


        #region "Lifecycle"

        /// <summary>
        /// Create the Instance's components (once). Start loads them.
        /// </summary>
        public async Task Initialize()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                InitializeCore();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task Start()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                InitializeCore();

                if (_started) return;

                var logger = GetLogger();

                SetStatus(TrakHoundInstanceStatus.Starting);
                Starting?.Invoke(this, EventArgs.Empty);

                // Recorded in the Boot History (how long each step took, and the Warnings and Errors logged)
                var runtimeStart = new TrakHoundInstanceRuntimeStart { Started = DateTime.UtcNow };
                var steps = new TrakHoundBootStepTimer();
                using var startLog = new TrakHoundBootLogCollector(_logProvider);

                try
                {
                    // Runtime components are Disposed by Stop, so recreate them when Starting again
                    if (!_runtimeCreated)
                    {
                        steps.Begin("Create Runtime");
                        CreateRuntimeComponents(logger);
                    }

                    // Variables are read on each Start (so a Restart applies changes, ex. from a Deployment)
                    steps.Begin("Load Configuration Variables");
                    var variableCount = TrakHoundConfigurationVariables.Load(_configurationProfile.Path);
                    logger?.LogInformation($"Configuration Variables Loaded : {variableCount}");

                    steps.Begin("Load Packages");
                    logger?.LogInformation("Loading Packages...");
                    _packageManager.VerificationMode = _configuration.PackageVerification;
                    _packageManager.Load();

                    steps.Begin("Load Services");
                    logger?.LogInformation("Loading Services...");
                    _serviceProvider.Load();

                    steps.Begin("Load Service Routers");
                    logger?.LogInformation("Loading Service Routers...");
                    _serviceRouterProvider.Load();

                    steps.Begin("Load Identity Providers");
                    logger?.LogInformation("Loading Identity Providers...");
                    _securityManager.Load();

                    steps.Begin("Load Apis");
                    logger?.LogInformation("Loading Apis...");
                    await _apiProvider.Load();

                    steps.Begin("Load Functions");
                    logger?.LogInformation("Loading Functions...");
                    _functionManager.Load();

                    steps.Begin("Load Workers");
                    logger?.LogInformation("Loading Workers...");
                    await _workerManager.Load();

                    steps.Begin("Start Workers");
                    _workerManager.StartAll();

                    steps.Begin("Load Apps");
                    logger?.LogInformation("Loading Apps...");
                    _appProvider.Load();
                    steps.End();

                    _started = true;
                    _startTime = DateTime.UtcNow;
                    runtimeStart.Success = true;
                    SetStatus(TrakHoundInstanceStatus.Started);
                    Started?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    steps.End(ex.Message);
                    runtimeStart.Error = $"{ex.GetType().Name} : {ex.Message}";

                    // Unload whatever was Loaded and report the Error (Start can be called again)
                    logger?.LogError($"Instance Start ERROR : {TrakHoundLogger.GetExceptionStackTrace(ex)}");

                    await UnloadRuntimeComponents(logger);
                    SetStatus(TrakHoundInstanceStatus.Error);
                }

                runtimeStart.Completed = DateTime.UtcNow;
                runtimeStart.Steps = steps.Steps;
                runtimeStart.Log = startLog.GetLog();
                startLog.Dispose();

                try { RuntimeStartCompleted?.Invoke(this, runtimeStart); }
                catch { } // A Boot History failure never affects the Start
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task Stop()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (!_started) return;

                var logger = GetLogger();

                logger?.LogInformation("Stopping Instance...");

                SetStatus(TrakHoundInstanceStatus.Stopping);
                Stopping?.Invoke(this, EventArgs.Empty);

                await UnloadRuntimeComponents(logger);

                _started = false;
                _stopTime = DateTime.UtcNow;
                logger?.LogInformation($"Instance Stopped : Running Time = {_stopTime - _startTime:d\\.hh\\:mm\\:ss}");
                SetStatus(TrakHoundInstanceStatus.Stopped);
                Stopped?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private void SetStatus(TrakHoundInstanceStatus status)
        {
            _lastUpdated = DateTime.UtcNow;
            _status = status;

            if (_instanceInformation != null)
            {
                _instanceInformation.Status = status;
                _instanceInformation.LastUpdated = _lastUpdated;
            }

            StatusUpdated?.Invoke(Id, status);
        }

        private void InitializeCore()
        {
            if (_initialized) return;

            if (_configuration == null) _configuration = TrakHoundInstanceConfiguration.Read();
            if (_configuration == null)
            {
                _configuration = new TrakHoundInstanceConfiguration();
                _configuration.Save();
            }

            var logger = GetLogger();

            _instanceInformation = CreateInstanceInformation();
            SetStatus(TrakHoundInstanceStatus.Initializing);

            // Licenses are kept in the configuration profile
            TrakHoundLicenseManagers.ManagerAdded += (s, publisherId) => logger?.LogInformation($"License Manager Loaded : Publisher ID = {publisherId}");
            TrakHoundLicenseManagers.Initialize(Path.Combine(_configurationPath, TrakHoundLicenseManagers.DirectoryName));

            var deviceId = TrakHoundDeviceId.GetInformation();
            logger?.LogInformation($"Device ID : {deviceId.DeviceId} (from the {(deviceId.Source == TrakHoundDeviceIdSource.Variable ? TrakHoundDeviceId.Variable : "machine name")})");
            if (deviceId.Warning != null) logger?.LogWarning(deviceId.Warning);

            // Configuration Parameters that can't be decrypted (ex. copied from a different Instance)
            TrakHoundSecrets.DecryptionFailed += (s, message) => logger?.LogWarning(message);

            // Secret Configuration Parameters that had to be saved as plain text (ex. the key file can't be read)
            TrakHoundSecrets.EncryptionFailed += (s, message) => logger?.LogError(message);

            // Configuration Parameters that reference a Variable that isn't defined
            TrakHoundConfigurationVariables.VariableNotFound += (s, message) => logger?.LogWarning(message);

            // Initialize Package Manager
            _packageManager = new TrakHoundPackageManager(null, (TrakHoundPackageClient)null, _packagesPath);
            _packageManager.PackageAdded += (s, args) => { logger?.LogDebug($"Package Loaded : {args.Id} v{args.Version}"); };
            _packageManager.TrustedPublishersProvider = () => TrakHoundTrustedPublishers.Combine(TrakHoundTrustedPublishers.Read(_configurationProfile.Path), AdditionalTrustedPublishersProvider?.Invoke());
            _packageManager.ApprovalsProvider = () => PackageApprovalsProvider?.Invoke();
            _packageManager.ApprovalsRefresh = token => PackageApprovalsRefresh != null ? PackageApprovalsRefresh(token) : Task.CompletedTask;
            _packageManager.InUsePackagesProvider = GetInUsePackageUuids;
            _packageManager.PackageVerified += (s, args) => LogPackageVerified(logger, args);

            // Load Configuration (unless a Configuration Profile was provided)
            if (_configurationProfile == null)
            {
                logger?.LogInformation($"Loading Configuration Profile ({_configurationProfileId})...");
                _configurationProfile = new TrakHoundConfigurationProfile(_configurationProfileId, _packageManager, _configurationPath);
                _configurationProfile.ConfigurationAdded += (s, args) => { logger?.LogDebug($"Configuration Loaded : {args.Category} : {args.Id}"); };
                _configurationProfile.ErrorReceived += (s, message) => logger?.LogError(message);
                _configurationProfile.Load<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHound.Zones.TrakHoundZoneConfiguration>(TrakHound.Zones.TrakHoundZoneConfiguration.ConfigurationCategory);
                _configurationProfile.Load<TrakHound.Groups.TrakHoundGroupConfiguration>(TrakHound.Groups.TrakHoundGroupConfiguration.ConfigurationCategory);
            }

            // Audit Log (compares with the last known state before anything else can change it)
            _auditMonitor?.Dispose();
            _auditMonitor = new TrakHound.Instance.Audit.TrakHoundAuditMonitor(new TrakHound.Instance.Audit.TrakHoundAuditLog(_configurationProfile.Path), _configurationProfile.Path, _packageManager.Path, _packageManager, _configurationProfile as TrakHoundConfigurationProfile, logger);
            _auditMonitor.Start();

            // Core components (live for the lifetime of the Instance, so references to them, ex. in Dependency Injection, stay valid)
            _moduleProvider = new TrakHoundModuleProvider(_packageManager, _logProvider);
            _volumeProvider = new TrakHoundVolumeProvider(_volumesPath);
            _variableProvider = new TrakHoundVariableProvider(_variablesPath);
            _apiConnectionManager = new TrakHoundApiConnectionManager();
            _zoneManager = new TrakHound.Zones.TrakHoundZoneManager(_configurationProfile, _logProvider, _packageManager);
            _moduleTokens = new TrakHoundModuleTokenManager(() => TrakHoundModuleTokenManager.GetInstanceUrl(_configuration));
            _streamManager = new TrakHoundApiStreamManager();
            _clientProvider = new TrakHoundInstanceClientProvider(this, null, _volumeProvider);

            _securityManager = new TrakHoundSecurityManager(_configurationProfile, _moduleProvider, _clientProvider, _volumeProvider, _variableProvider, _packageManager, _logProvider?.GetLoggerByName(TrakHoundSecurityManager.LoggerName));
            _securityManager.IdentityProviderAdded += (s, args) => { logger?.LogInformation($"Identity Provider Loaded : {args.Id}"); };

            // Runtime components are created here too so they are available before the Instance is Started (ex. for Dependency Injection setup)
            CreateRuntimeComponents(logger);

            _initialized = true;
            SetStatus(TrakHoundInstanceStatus.Initialized);

            // Evaluate Health in the background (for the life of the process)
            _health.Start();
        }

        private TrakHoundInstanceInformation CreateInstanceInformation()
        {
            var information = new TrakHoundInstanceInformation();
            information.Id = _configuration.InstanceId;
            information.Version = _version;
            information.Name = _configuration.Name;
            information.Description = _configuration.Description;
            information.Environment.Sender = Environment.MachineName;
            information.Environment.User = Environment.UserName;
            information.Environment.OperatingSystem.Type = Environment.OSVersion.Platform.ToString();
            information.Environment.OperatingSystem.Name = Environment.OSVersion.VersionString;
            information.Environment.OperatingSystem.Version = Environment.OSVersion.Version.ToString();

            var httpInterface = new TrakHoundInstanceInterfaceInformation();
            httpInterface.Id = "http-01";
            httpInterface.Type = "HTTP";
            httpInterface.Address = !string.IsNullOrEmpty(_configuration.HttpAddress) ? _configuration.HttpAddress : GetAddress();
            httpInterface.Port = _configuration.HttpPort;
            information.Interfaces = new List<TrakHoundInstanceInterfaceInformation> { httpInterface };

            return information;
        }

        /// <summary>
        /// Create the Runtime components (Services, Routers, APIs, Apps, Functions, Workers) and connect them to the Client Provider
        /// </summary>
        private void CreateRuntimeComponents(ITrakHoundLogger logger)
        {
            var instanceId = _configuration.InstanceId;

            // Metrics settings are read when the Runtime is created (so changes are applied on restart)
            _metrics = _configuration.Metrics?.Clone() ?? new TrakHoundInstanceMetricsConfiguration();
            _apiConnectionManager.MetricsEnabled = _metrics.ApiEnabled;
            _apiConnectionManager.RequestHistory = _metrics.ApiRequestHistory; // Kept even when Metrics Collection is Disabled
            _streamManager.ClosedHistory = _metrics.ApiStreamHistory;
            _streamMetrics.MetricsEnabled = _metrics.StreamsEnabled;
            _mcpMetrics.MetricsEnabled = _metrics.McpEnabled;
            _httpMetrics.MetricsEnabled = _metrics.HttpEnabled;
            _httpRequests.Capacity = _metrics.HttpRequestHistory; // Kept even when Metrics Collection is Disabled
            TrakHoundModuleCpuMonitors.Default.Enabled = _metrics.CpuEnabled; // Workers, Functions, APIs and Apps
            if (!_metrics.Enabled) logger?.LogInformation("Metrics Collection Disabled");

            // Service Provider
            var serviceProvider = new Services.TrakHoundServiceProvider(instanceId, _configurationProfile, _moduleProvider, _volumeProvider, _variableProvider, _logProvider, _packageManager);
            serviceProvider.ServiceAdded += (s, args) => { logger?.LogInformation($"Service Loaded : {args.Id}"); };
            serviceProvider.ServiceLoadError += (s, ex) => { logger?.LogError($"Service Load ERROR : {ex.Message}"); };
            _serviceProvider = serviceProvider;

            // Router Provider
            var routerProvider = new TrakHoundServiceRouterProvider(_configurationProfile, _logProvider, serviceProvider);
            routerProvider.MetricsEnabled = _metrics.ServicesEnabled;
            routerProvider.RouterAdded += (s, args) => { logger?.LogInformation($"Service Router Loaded : {args.Configuration?.Id} => {args.Configuration?.Name}"); };
            routerProvider.RouterRemoved += (s, args) => { logger?.LogInformation($"Service Router Removed : {args}"); };
            _serviceRouterProvider = routerProvider;

            _servicesProvider = new TrakHoundServicesProvider(serviceProvider, routerProvider);
            _clientProvider.ServicesProvider = _servicesProvider;

            // Api Provider
            _apiProvider = new TrakHoundApiProvider(this, _configurationProfile, _moduleProvider, _clientProvider, _volumeProvider, _variableProvider, _logProvider, _packageManager, _apiConnectionManager, _streamManager);
            _apiProvider.ApiAdded += (s, args) => { logger?.LogInformation($"Api Loaded : {args.PackageId}: {args.PackageVersion} : {args.Id} => {args.Route}"); };
            _apiProvider.ApiRemoved += (s, args) => { logger?.LogInformation($"Api Removed : {args}"); };
            _apiProvider.ApiRemoved += (s, args) => { _moduleTokens.RevokeAll(TrakHound.Zones.TrakHoundZoneResources.GetMemberId(TrakHound.Zones.TrakHoundZoneResources.ApiPrefix, args)); };
            _clientProvider.ApiProvider = _apiProvider;

            // App Provider
            _appProvider = new TrakHoundAppProvider(this, _configurationProfile, _moduleProvider, _clientProvider, _volumeProvider, _variableProvider, _logProvider, _packageManager, GetServiceProvider);
            _appProvider.Metrics.Enabled = _metrics.AppsEnabled;
            _appProvider.AppAdded += (s, args) => { logger?.LogInformation($"App Loaded : {args.PackageId}: {args.PackageVersion} : {args.Id} => {args.Route}"); };
            _appProvider.AppRemoved += (s, args) => { logger?.LogInformation($"App Removed : {args}"); };
            _clientProvider.AppProvider = _appProvider;

            // Function Manager
            _functionManager = new TrakHoundFunctionManager(this, _configurationProfile, _moduleProvider, _clientProvider, _volumeProvider, _variableProvider, _logProvider, _packageManager);
            _functionManager.MetricsEnabled = _metrics.FunctionsEnabled;
            _functionManager.CpuMonitoringEnabled = _metrics.CpuEnabled;
            _functionManager.EngineAdded += (s, args) => { logger?.LogInformation($"Function Engine Loaded : {args.PackageId}: {args.PackageVersion} : {args?.EngineId}"); };
            _functionManager.EngineRemoved += (s, args) => { logger?.LogInformation($"Function Engine Removed : {args}"); };
            _clientProvider.FunctionManager = _functionManager;

            // Worker Manager
            _workerManager = new TrakHoundWorkerManager(this, _configurationProfile, _moduleProvider, _clientProvider, _volumeProvider, _variableProvider, _logProvider, _packageManager);
            _workerManager.MetricsEnabled = _metrics.WorkersEnabled;
            _workerManager.CpuMonitoringEnabled = _metrics.CpuEnabled;
            _workerManager.EngineAdded += (s, args) => { logger?.LogInformation($"Worker Engine Loaded : {args.PackageId}: {args.PackageVersion} : {args?.EngineId}"); };
            _workerManager.EngineRemoved += (s, args) => { logger?.LogInformation($"Worker Engine Removed : {args}"); };
            _clientProvider.WorkerManager = _workerManager;

            _runtimeCreated = true;
        }

        /// <summary>
        /// Dispose the Runtime components. Each is Disposed separately so one failure doesn't prevent the others from being Disposed.
        /// The (Disposed) references are kept so callers (ex. route checks) don't need to handle null while Stopped.
        /// </summary>
        private async Task UnloadRuntimeComponents(ITrakHoundLogger logger)
        {
            if (!_runtimeCreated) return;

            await DisposeComponent("Audit Log", logger, () => { _auditMonitor?.Dispose(); return Task.CompletedTask; });
            await DisposeComponent("Apps", logger, () => { _appProvider?.Dispose(); return Task.CompletedTask; });
            await DisposeComponent("Workers", logger, async () => { if (_workerManager != null) await _workerManager.DisposeAsync(); });
            await DisposeComponent("Functions", logger, () => { _functionManager?.Dispose(); return Task.CompletedTask; });
            await DisposeComponent("Apis", logger, () => { _apiProvider?.Dispose(); return Task.CompletedTask; });
            await DisposeComponent("Services", logger, () => { _servicesProvider?.Dispose(); return Task.CompletedTask; });

            _runtimeCreated = false;
        }

        private static async Task DisposeComponent(string name, ITrakHoundLogger logger, Func<Task> dispose)
        {
            try
            {
                await dispose();
            }
            catch (Exception ex)
            {
                logger?.LogError($"Instance Stop ERROR : {name} : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        #endregion


        private void LogReceived(object sender, TrakHoundLogItem logItem)
        {
            LogUpdated?.Invoke(Id, logItem);
        }

        private IServiceProvider GetServiceProvider()
        {
            return ServiceProvider;
        }


        #region "Packages"

        private static void LogPackageVerified(ITrakHoundLogger logger, TrakHoundPackageVerifiedArgs args)
        {
            if (logger == null) return;

            if (args.TrustedPublishersError != null) logger.LogError($"Trusted Publishers can't be read, so no signature is trusted : {args.TrustedPublishersError}");

            var message = $"Package Verification : {args.Package.Id} v{args.Package.Version} : {TrakHoundPackageVerificationPolicy.Describe(args.Result)}";

            if (!args.Allowed) logger.LogError($"{message} : Refused");
            else if (args.Result.Status == TrakHoundPackageVerificationStatus.Invalid) logger.LogWarning(message);
            else if (args.Result.SignatureStatus == TrakHoundPackageSignatureStatus.Trusted) logger.LogDebug(message);
            else logger.LogInformation(message); // Unsigned Packages are normal unless Require is set
        }

        public async Task InstallPackage(TrakHoundPackage package, bool downloadPackage = true, TrakHoundPackageClient packageClient = null)
        {
            if (package == null) return;

            // Install Package
            if (downloadPackage)
            {
                var packageResults = await _packageManager.Install(package.Uuid, packageClient);
                if (packageResults.IsNullOrEmpty()) return;
            }

            // Add a default Configuration for the Package (if it doesn't already have one)
            var configuration = TrakHoundPackageConfigurations.CreateDefault(package);
            if (configuration != null)
            {
                var configurations = _configurationProfile.Get<ITrakHoundConfiguration>(configuration.Category).ToList();
                if (!configurations.Any(o => TrakHoundPackageConfigurations.GetPackageId(o) == package.Id))
                {
                    _configurationProfile.Add(configuration, true);
                }

                // Existing Configurations without a Volume ID get the Package's default (ex. one added by this version)
                foreach (var existing in configurations)
                {
                    var updated = TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, package);
                    if (updated != null)
                    {
                        _configurationProfile.Add(updated, true);
                        GetLogger()?.LogInformation($"Configuration ({updated.Category} : {updated.Id}) now uses the default Volume of {package.Id} v{package.Version}");
                    }
                }
            }
        }

        /// <summary>
        /// Uninstall Packages that are not used by any Configuration
        /// </summary>
        public void CleanPackages()
        {
            var unusedPackages = GetUnusedPackages();
            if (!unusedPackages.IsNullOrEmpty())
            {
                foreach (var package in unusedPackages)
                {
                    _packageManager.Uninstall(package.Id, package.Version);
                }
            }
        }

        /// <summary>
        /// Get the installed Packages that are not used by any Configuration (what CleanPackages removes)
        /// </summary>
        public IEnumerable<TrakHoundPackage> GetUnusedPackages()
        {
            var unusedPackages = new List<TrakHoundPackage>();

            // Use the Instance's Package Path (which may be set by an Environment Variable or Command Line Option)
            var installedPackages = TrakHoundPackage.GetInstalled(_packagesPath);
            if (!installedPackages.IsNullOrEmpty())
            {
                var activePackageUuids = GetActivePackages();
                var revertPackageUuids = GetRevertPackages(installedPackages.Select(o => o.Id).Distinct(), activePackageUuids);

                foreach (var installedPackage in installedPackages)
                {
                    // Configuration Packages are applied by the Configuration Profile (not referenced by a Configuration)
                    if (IsProfilePackage(installedPackage)) continue;

                    // The previous Version of a used Package is kept so the Package can be reverted to it
                    if (revertPackageUuids.Contains(installedPackage.Uuid)) continue;

                    if (!activePackageUuids.Contains(installedPackage.Uuid))
                    {
                        unusedPackages.Add(installedPackage);
                    }
                }
            }

            return unusedPackages;
        }

        /// <summary>
        /// The UUIDs of the installed Package versions that are in use : used by a Configuration (or an open App Session), or applied by the Configuration Profile
        /// </summary>
        private IEnumerable<string> GetInUsePackageUuids()
        {
            var uuids = new HashSet<string>(GetPackageUsage().Select(o => o.PackageUuid).Where(o => o != null));

            var profilePackages = _packageManager?.GetByCategory(_configurationPackageCategory);
            if (profilePackages != null) uuids.UnionWith(profilePackages.Select(o => o.Uuid));

            return uuids;
        }

        /// <summary>
        /// Returns true if the Package is applied by the Configuration Profile (and is not referenced by a Configuration)
        /// </summary>
        public bool IsProfilePackage(TrakHoundPackage package)
        {
            return package != null && package.Category == _configurationPackageCategory;
        }

        /// <summary>
        /// Get the Configurations that use each installed Package (Versions such as "*" resolve to the installed Version used)
        /// </summary>
        public IEnumerable<TrakHoundPackageUsage> GetPackageUsage()
        {
            var usages = new List<TrakHoundPackageUsage>();

            AddPackageUsage<TrakHoundApiConfiguration>(usages, TrakHoundApiConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => o.Name);
            AddPackageUsage<TrakHoundAppConfiguration>(usages, TrakHoundAppConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => o.Name);
            AddPackageUsage<TrakHoundServiceConfiguration>(usages, TrakHoundServiceConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => o.Name);
            AddPackageUsage<TrakHoundFunctionConfiguration>(usages, TrakHoundFunctionConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => o.Name);
            AddPackageUsage<TrakHoundIdentityProviderConfiguration>(usages, TrakHoundIdentityProviderConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => null);
            AddPackageUsage<TrakHoundWorkerConfiguration>(usages, TrakHoundWorkerConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion, o => o.Name);

            // App Versions still used by open Sessions (ex. the previous Version after an App was updated) so they aren't Uninstalled while in use
            foreach (var versionSessions in GetAppVersionSessions())
            {
                var packageUuid = TrakHoundPackage.GenerateUuid(versionSessions.PackageId, versionSessions.PackageVersion);
                if (usages.Any(o => o.PackageUuid == packageUuid && o.ConfigurationId == versionSessions.ConfigurationId)) continue;

                var configuration = _configurationProfile?.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory, versionSessions.ConfigurationId);
                var sessions = versionSessions.SessionCount == 1 ? "1 open session" : $"{versionSessions.SessionCount} open sessions";

                usages.Add(new TrakHoundPackageUsage
                {
                    PackageUuid = packageUuid,
                    PackageId = versionSessions.PackageId,
                    PackageVersion = versionSessions.PackageVersion,
                    RequestedVersion = versionSessions.PackageVersion,
                    ConfigurationCategory = TrakHoundAppConfiguration.ConfigurationCategory,
                    ConfigurationId = versionSessions.ConfigurationId,
                    ConfigurationName = $"{configuration?.Name ?? versionSessions.ConfigurationId} ({sessions})"
                });
            }

            return usages;
        }

        /// <summary>
        /// Get whether the Package can be reverted to an older installed Version (the previous Version if not specified).
        /// Removing the Versions above the Target moves Configurations that request the latest (ex. "*") to the Target Version,
        /// so a Revert is only allowed when every Configuration using a removed Version also accepts the Target Version.
        /// </summary>
        public TrakHoundPackageRevert GetPackageRevert(string packageId, string targetVersion = null)
        {
            return GetPackageRevert(packageId, targetVersion, null);
        }

        /// <summary>
        /// Get the Revert for each older installed Version of the Package (highest first)
        /// </summary>
        public IEnumerable<TrakHoundPackageRevert> GetPackageRevertOptions(string packageId)
        {
            var current = _packageManager?.GetLatest(packageId);
            if (current == null) return Enumerable.Empty<TrakHoundPackageRevert>();

            // Read the Usages once for all of the options
            var usages = GetPackageUsage().Where(o => o.PackageId == packageId).ToList();

            return GetOlderVersions(packageId, current.Version)
                .Select(o => GetPackageRevert(packageId, o, usages))
                .ToList();
        }

        private TrakHoundPackageRevert GetPackageRevert(string packageId, string targetVersion, List<TrakHoundPackageUsage> usages)
        {
            var revert = new TrakHoundPackageRevert { PackageId = packageId };

            var current = _packageManager?.GetLatest(packageId);
            if (current == null)
            {
                revert.Message = "Not Installed";
                return revert;
            }

            revert.CurrentVersion = current.Version;

            var olderVersions = GetOlderVersions(packageId, current.Version);
            revert.TargetVersion = string.IsNullOrEmpty(targetVersion) ? olderVersions.FirstOrDefault() : olderVersions.FirstOrDefault(o => o == targetVersion);

            if (revert.TargetVersion == null)
            {
                revert.Message = string.IsNullOrEmpty(targetVersion) ? "No previous version is installed" : $"v{targetVersion} is not an older installed version";
                return revert;
            }

            revert.RemovedVersions = _packageManager.GetVersions(packageId)
                .Where(o => VersionComparer.Default.Compare(o, revert.TargetVersion) > 0)
                .OrderByDescending(o => o, VersionComparer.Default)
                .ToList();

            if (IsProfilePackage(current))
            {
                revert.Message = "Applied by the Configuration Profile";
                return revert;
            }

            // Configurations (or open App Sessions) using a Version that will be removed
            var removedUuids = new HashSet<string>(revert.RemovedVersions.Select(o => TrakHoundPackage.GenerateUuid(packageId, o)));
            var affected = (usages ?? GetPackageUsage().ToList()).Where(o => o.PackageUuid != null && removedUuids.Contains(o.PackageUuid)).ToList();

            // Those that require a removed Version specifically
            var pinned = affected.Where(o => !AcceptsVersion(o.RequestedVersion, revert.TargetVersion)).ToList();
            if (pinned.Count > 0)
            {
                var names = string.Join(", ", pinned.Select(o => $"{o.ConfigurationCategory.ToTitleCase()} '{o.ConfigurationName ?? o.ConfigurationId}' (v{o.PackageVersion})"));
                revert.Message = $"Required by {names}";
            }
            else
            {
                revert.ConfigurationCount = affected.Count;
                revert.CanRevert = true;
            }

            return revert;
        }

        /// <summary>
        /// Revert the Package to an older installed Version (the previous Version if not specified) by removing the Versions above it.
        /// Configurations using a removed Version reload with the Target Version.
        /// </summary>
        public TrakHoundPackageRevert RevertPackage(string packageId, string targetVersion = null)
        {
            lock (_packageRevertLock)
            {
                // Check again (Configurations or installed Versions may have changed)
                var revert = GetPackageRevert(packageId, targetVersion);
                if (revert.CanRevert)
                {
                    // Remove the highest first, so the latest Version only ever moves down
                    foreach (var version in revert.RemovedVersions)
                    {
                        if (!_packageManager.Uninstall(packageId, version))
                        {
                            revert.CanRevert = false;
                            revert.Message = $"v{version} could not be removed";
                            GetLogger()?.LogWarning($"Package Revert Failed : {packageId} v{revert.CurrentVersion} => v{revert.TargetVersion} : v{version} could not be removed");
                            return revert;
                        }
                    }

                    GetLogger()?.LogInformation($"Package Reverted : {packageId} v{revert.CurrentVersion} => v{revert.TargetVersion} (Removed {string.Join(", ", revert.RemovedVersions.Select(o => $"v{o}"))})");
                }

                return revert;
            }
        }

        /// <summary>
        /// The installed Versions of the Package below the specified Version (highest first)
        /// </summary>
        private List<string> GetOlderVersions(string packageId, string version)
        {
            return _packageManager?.GetVersions(packageId)?
                .Where(o => VersionComparer.Default.Compare(o, version) < 0)
                .OrderByDescending(o => o, VersionComparer.Default)
                .ToList() ?? new List<string>();
        }


        /// <summary>
        /// The previous Version of each used Package (the Version a Revert would go back to)
        /// </summary>
        private HashSet<string> GetRevertPackages(IEnumerable<string> packageIds, HashSet<string> activePackageUuids)
        {
            var revertPackageUuids = new HashSet<string>();

            foreach (var packageId in packageIds)
            {
                var current = _packageManager?.GetLatest(packageId);
                if (current == null || !activePackageUuids.Contains(current.Uuid)) continue;

                var previousVersion = GetOlderVersions(packageId, current.Version).FirstOrDefault();
                if (previousVersion != null) revertPackageUuids.Add(TrakHoundPackage.GenerateUuid(packageId, previousVersion));
            }

            return revertPackageUuids;
        }

        /// <summary>
        /// Whether a Configuration's requested Version (ex. "*", "1.*", "1.2.3") also resolves to the specified Version
        /// </summary>
        private static bool AcceptsVersion(string requestedVersion, string version)
        {
            if (string.IsNullOrEmpty(requestedVersion) || requestedVersion == "*") return true;
            return requestedVersion.Contains('*') && StringFunctions.MatchVersion(version, requestedVersion);
        }

        private void AddPackageUsage<TConfiguration>(List<TrakHoundPackageUsage> usages, string category, Func<TConfiguration, string> getPackageId, Func<TConfiguration, string> getPackageVersion, Func<TConfiguration, string> getName) where TConfiguration : ITrakHoundConfiguration
        {
            var configurations = _configurationProfile?.Get<TConfiguration>(category);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    var packageId = getPackageId(configuration);
                    if (string.IsNullOrEmpty(packageId)) continue;

                    var packageVersion = getPackageVersion(configuration);
                    var package = _packageManager.Resolve(packageId, packageVersion);

                    usages.Add(new TrakHoundPackageUsage
                    {
                        PackageUuid = package?.Uuid,
                        PackageId = packageId,
                        PackageVersion = package?.Version,
                        RequestedVersion = packageVersion,
                        ConfigurationCategory = category,
                        ConfigurationId = configuration.Id,
                        ConfigurationName = getName(configuration)
                    });
                }
            }
        }

        private HashSet<string> GetActivePackages()
        {
            var activePackageUuids = new HashSet<string>();

            AddActivePackages<TrakHoundApiConfiguration>(activePackageUuids, TrakHoundApiConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);
            AddActivePackages<TrakHoundAppConfiguration>(activePackageUuids, TrakHoundAppConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);
            AddActivePackages<TrakHoundServiceConfiguration>(activePackageUuids, TrakHoundServiceConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);
            AddActivePackages<TrakHoundFunctionConfiguration>(activePackageUuids, TrakHoundFunctionConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);
            AddActivePackages<TrakHoundIdentityProviderConfiguration>(activePackageUuids, TrakHoundIdentityProviderConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);
            AddActivePackages<TrakHoundWorkerConfiguration>(activePackageUuids, TrakHoundWorkerConfiguration.ConfigurationCategory, o => o.PackageId, o => o.PackageVersion);

            // App Versions still used by open Sessions (ex. the previous Version after an App was updated)
            foreach (var versionSessions in GetAppVersionSessions())
            {
                activePackageUuids.Add(TrakHoundPackage.GenerateUuid(versionSessions.PackageId, versionSessions.PackageVersion));
            }

            return activePackageUuids;
        }

        private IEnumerable<TrakHoundAppVersionSessions> GetAppVersionSessions()
        {
            var injectionServiceManager = _appProvider?.InjectionServiceManager as TrakHoundAppInjectionServiceManager;
            return injectionServiceManager?.GetVersionSessions() ?? Enumerable.Empty<TrakHoundAppVersionSessions>();
        }

        private void AddActivePackages<TConfiguration>(HashSet<string> activePackageUuids, string category, Func<TConfiguration, string> getPackageId, Func<TConfiguration, string> getPackageVersion) where TConfiguration : ITrakHoundConfiguration
        {
            var configurations = _configurationProfile.Get<TConfiguration>(category);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    var packageUuid = GetPackageUuid(getPackageId(configuration), getPackageVersion(configuration));
                    if (packageUuid != null) activePackageUuids.Add(packageUuid);
                }
            }
        }

        private string GetPackageUuid(string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(packageId)) return null;

            var package = _packageManager.Resolve(packageId, packageVersion);

            return package?.Uuid;
        }

        #endregion


        /// <summary>
        /// Get the IPv4 Address of the network interface used to reach other networks (one with a Gateway), ignoring virtual adapters without one.
        /// </summary>
        private static string GetAddress()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var properties = networkInterface.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(o => o.Address.AddressFamily == AddressFamily.InterNetwork && !o.Address.Equals(IPAddress.Any));
                    if (!hasGateway) continue;

                    var address = properties.UnicastAddresses.FirstOrDefault(o => o.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (address != null) return address.Address.ToString();
                }
            }
            catch { }

            // Fallback to the first IPv4 Address of the Host
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList.FirstOrDefault(o => o.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            }
            catch { }

            return null;
        }
    }
}
