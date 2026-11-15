// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Reflection;
using System.Text.RegularExpressions;
using TrakHound.Apps;
using TrakHound.Blazor.Routing;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Extensions;
using TrakHound.Groups;
using TrakHound.Instance.Apps;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Blazor.Apps
{
    public class TrakHoundAppProvider : ITrakHoundAppProvider, IDisposable
    {
        private const string _packageCategory = "app";
        private const string _packagesDirectory = "_packages";
        private const string _defaultThemeMetadataName = ".defaultTheme";

        private const string _routeParameterRegexPattern = @"(\{(.*?)\})";
        private static readonly Regex _routeParameterRegex = new Regex(_routeParameterRegexPattern, RegexOptions.Compiled);

        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        //private readonly ITrakHoundPageRouteManager _routeManager;
        //private readonly ITrakHoundAppInjectionServiceManager _injectionServiceManager;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly ITrakHoundModuleProvider _moduleProvider;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly TrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly ITrakHoundLogger _logger;
        private readonly Func<IServiceProvider> _getServiceProvider;
        private readonly TrakHoundModuleContext _moduleContext;
        private readonly string _baseUrl;
        private readonly Dictionary<string, TrakHoundAppInformation> _appInformations = new Dictionary<string, TrakHoundAppInformation>();
        private readonly Dictionary<string, TrakHoundPackage> _appPackages = new Dictionary<string, TrakHoundPackage>();
        private readonly Dictionary<string, ModuleInfo> _moduleInfos = new Dictionary<string, ModuleInfo>();
        private readonly ListDictionary<string, string> _packageStylesheets = new ListDictionary<string, string>();
        private readonly ListDictionary<string, string> _packageScripts = new ListDictionary<string, string>();
        private readonly Dictionary<string, string> _installedConfigurationHashes = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(); // Configuration.Id => Module.Hash
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _routeRegexes = new System.Collections.Concurrent.ConcurrentDictionary<string, Regex>(); // Route => Route Regex
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(5000);
        private readonly object _lock = new object();
        private readonly object _loadLock = new object();
        private readonly Dictionary<string, string> _copiedPackages = new Dictionary<string, string>(); // File Type + Destination Directory => Package Hash + Location (the files already in place)

        private string _loadedState; // The App Configurations and Packages of the last successful Load (to skip a Load when nothing changed)
        private bool _loadFailed; // Set when part of a Load fails, so the next Load runs again instead of being skipped

        private volatile TrakHoundAppPageRouteManager _routeManager;
        private volatile TrakHoundAppInjectionServiceManager _injectionServiceManager;


        public ITrakHoundPageRouteManager RouteManager => _routeManager;

        /// <summary>
        /// Page Views and Durations for each Client Connection (recorded by the App Router)
        /// </summary>
        public TrakHoundAppMetricsCollector Metrics { get; } = new TrakHoundAppMetricsCollector();

        public ITrakHoundAppInjectionServiceManager InjectionServiceManager => _injectionServiceManager;

        public IEnumerable<string> PackageStylesheets => _packageStylesheets.Values;

        public IEnumerable<string> PackageScripts => _packageScripts.Values;


        public event EventHandler<TrakHoundAppInformation> AppAdded;

        public event EventHandler<string> AppRemoved;

        public event TrakHoundAppRefreshHandler AppRefreshRequested;

        public event TrakHoundAppRefreshHandler ConnectionRefreshRequested;

        /// <summary>
        /// Raised to ask a Connection's page to reload when convenient (it shows a banner and reloads on the next navigation or when idle)
        /// </summary>
        public event EventHandler<string> ConnectionReloadNotifyRequested;

        /// <summary>
        /// Raised to ask every open page of an App to reload when convenient (each shows a banner and reloads on the next navigation or when idle)
        /// </summary>
        public event EventHandler<string> AppReloadNotifyRequested;

        /// <summary>
        /// Raised to show a connection's Code on its page (for one connection, or for every open page of an App)
        /// </summary>
        public event EventHandler<TrakHoundAppIdentifyRequest> IdentifyRequested;

        /// <summary>
        /// Raised to show a Message from an Administrator on a connection's page (for one connection, or for every open page of an App)
        /// </summary>
        public event EventHandler<TrakHoundAppMessageRequest> MessageRequested;


        public bool IsRouteValid(string route)
        {
            if (string.IsNullOrEmpty(route)) route = "/";

            // The Route Manager is Initialised when Apps are Loaded (not on every request)
            var routeManager = _routeManager;
            if (routeManager != null)
            {
                return routeManager.Match(route).IsMatch;
            }

            return false;
        }


        public TrakHoundAppProvider(
            ITrakHoundInstance instance,
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundClientProvider clientProvider,
            TrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            TrakHoundPackageManager packageManager,
            Func<IServiceProvider> getServiceProvider
            )
        {
            _instance = instance;
            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;
            _logger = logProvider?.GetLoggerByName("app");

            // Access Log entry for each App Page View
            Metrics.PageViewEnded += new TrakHoundAppAccessLog(logProvider).Write;

            _getServiceProvider = getServiceProvider;

            _delayLoadEvent.Elapsed += UpdateDelayElapsed;

            // Set Base URL (HTTP Address)
            if (_instance != null)
            {
                _baseUrl = _instance.Information.GetInterface("HTTP")?.GetBaseUrl();
            }

            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

            //_routeManager = routeManager;
            //_injectionServiceManager = injectionServiceManager;
            _packageManager = packageManager;
            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;

            var contextId = Guid.NewGuid().ToString();
            _moduleContext = new TrakHoundModuleContext(contextId);

            _moduleProvider = moduleProvider;
            _moduleManager = moduleProvider.Get<ComponentBase>(_packageCategory, _moduleContext);

            //Load();
        }

        public TrakHoundAppProvider(
            ITrakHoundInstance instance,
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundClientProvider clientProvider,
            TrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            TrakHoundPackageManager packageManager,
            Func<IServiceProvider> getServiceProvider,
            TrakHoundModuleContext context
            )
        {
            _instance = instance;
            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;
            _logger = logProvider?.GetLoggerByName("app");

            // Access Log entry for each App Page View
            Metrics.PageViewEnded += new TrakHoundAppAccessLog(logProvider).Write;

            _getServiceProvider = getServiceProvider;

            _delayLoadEvent.Elapsed += UpdateDelayElapsed;

            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

            //_routeManager = routeManager;
            _packageManager = packageManager;
            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;

            _moduleContext = context;

            _moduleProvider = moduleProvider;
            _moduleManager = moduleProvider.Get<ComponentBase>(_packageCategory, context);

            //Load();
        }


        //public TrakHoundAppProvider(
        //    ITrakHoundInstance instance,
        //    ITrakHoundConfigurationProfile configurationProfile,
        //    ITrakHoundPageRouteManager routeManager,
        //    ITrakHoundAppInjectionServiceManager injectionServiceManager,
        //    TrakHoundPackageManager packageManager,
        //    ITrakHoundModuleProvider moduleProvider,
        //    ITrakHoundLogProvider logProvider
        //    )
        //{
        //    _instance = instance;
        //    _logProvider = logProvider;
        //    _delayLoadEvent.Elapsed += UpdateDelayElapsed;

        //    // Set Base URL (HTTP Address)
        //    if (_instance != null)
        //    {
        //        _baseUrl = _instance.Information.GetInterface("HTTP")?.GetBaseUrl();
        //    }

        //    _configurationProfile = configurationProfile;
        //    _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
        //    _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

        //    _routeManager = routeManager;
        //    _injectionServiceManager = injectionServiceManager;
        //    _packageManager = packageManager;
        //    _packageManager.PackageAdded += PackageAdded;
        //    _packageManager.PackageRemoved += PackageRemoved;

        //    _moduleManager = moduleProvider.Get<ComponentBase>(_packageCategory);

        //    Load();
        //}

        //public TrakHoundAppProvider(
        //    ITrakHoundInstance instance,
        //    ITrakHoundConfigurationProfile configurationProfile,
        //    ITrakHoundPageRouteManager routeManager,
        //    TrakHoundPackageManager packageManager,
        //    ITrakHoundModuleProvider moduleProvider,
        //    ITrakHoundLogProvider logProvider,
        //    TrakHoundModuleContext context
        //    )
        //{
        //    _instance = instance;
        //    _logProvider = logProvider;
        //    _delayLoadEvent.Elapsed += UpdateDelayElapsed;

        //    _configurationProfile = configurationProfile;
        //    _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
        //    _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

        //    _routeManager = routeManager;
        //    _packageManager = packageManager;
        //    _packageManager.PackageAdded += PackageAdded;
        //    _packageManager.PackageRemoved += PackageRemoved;

        //    _moduleManager = moduleProvider.Get<ComponentBase>(_packageCategory, context);

        //    Load();
        //}


        public void Dispose()
        {
            // The Configuration Profile and Package Manager outlive this Provider (a new one is created each time the Instance starts)
            _configurationProfile.ConfigurationAdded -= ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved -= ConfigurationUpdated;
            _packageManager.PackageAdded -= PackageAdded;
            _packageManager.PackageRemoved -= PackageRemoved;

            if (_delayLoadEvent != null)
            {
                _delayLoadEvent.Elapsed -= UpdateDelayElapsed;
                _delayLoadEvent.Dispose();
            }

            if (_routeManager != null) _routeManager.Dispose();
            if (_injectionServiceManager != null) _injectionServiceManager.Dispose();

            lock (_lock)
            {
                _appInformations.Clear();
                _appPackages.Clear();
                _moduleInfos.Clear();
                _packageStylesheets.Clear();
                _packageScripts.Clear();
                _installedConfigurationHashes.Clear();
                _installedModuleHashes.Clear();
            }

            ClearFiles();
        }



        private void ConfigurationUpdated(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration != null && configuration.Category == TrakHoundAppConfiguration.ConfigurationCategory)
            {
                _delayLoadEvent.Set();
            }
            else if (configuration != null && configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
            {
                // Reload if an App's Router came from the Group (the Load is skipped when nothing it reads has changed)
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



        public IEnumerable<TrakHoundAppInformation> GetInformation()
        {
            var informations = new List<TrakHoundAppInformation>();

            lock (_lock)
            {
                if (!_appInformations.IsNullOrEmpty())
                {
                    foreach (var appInformation in _appInformations)
                    {
                        informations.Add(appInformation.Value);
                    }
                }
            }

            return informations;
        }

        public TrakHoundAppInformation GetInformation(string appId)
        {
            // No App (ex. a route that isn't an App Page)
            if (string.IsNullOrEmpty(appId)) return null;

            lock (_lock)
            {
                if (!_appInformations.IsNullOrEmpty())
                {
                    var appInformation = _appInformations.GetValueOrDefault(appId);
                    if (appInformation != null)
                    {
                        return appInformation;
                    }
                }
            }

            return null;
        }

        public TrakHoundAppPageInformation GetPageInformation(string route)
        {
            var appInformations = GetInformation();
            if (!appInformations.IsNullOrEmpty())
            {
                var fMatchRoute = route?.TrimStart('/');

                foreach (var appInformation in appInformations)
                {
                    if (!appInformation.Pages.IsNullOrEmpty())
                    {
                        foreach (var pageInformation in appInformation.Pages)
                        {
                            var pageRoute = Url.Combine(appInformation.Route, pageInformation.Route);
                            var fPageRoute = pageRoute?.TrimStart('/');

                            if (IsRouteMatch(fMatchRoute, fPageRoute))
                            {
                                pageInformation.App = appInformation;
                                return pageInformation;
                            }
                        }
                    }
                }
            }

            return null;
        }


        public bool ReloadConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                var configurations = _configurationProfile.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
                if (!configurations.IsNullOrEmpty())
                {
                    var configuration = configurations.FirstOrDefault(o => o.Id == configurationId);
                    if (configuration != null)
                    {
                        // Don't run at the same time as a Load
                        lock (_loadLock)
                        {
                            // The next Load runs completely (ex. to rebuild the Route Manager) even if nothing else changed
                            _loadedState = null;

                            if (_injectionServiceManager != null)
                            {
                                _injectionServiceManager.ReloadServices(configuration.Id);
                            }

                            var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                            if (module != null)
                            {
                                ConfigureRoute(configuration, module);
                                SetInstalledHashes(configuration, module);

                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        public bool RefreshApp(string configurationId, int delay)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                if (AppRefreshRequested != null) AppRefreshRequested.Invoke(configurationId, delay);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Show the connection's Code on its page for a few seconds (to match a screen to its connection)
        /// </summary>
        public bool IdentifyConnection(string connectionId, int seconds = TrakHoundAppIdentifyRequest.DefaultSeconds)
        {
            if (string.IsNullOrEmpty(connectionId)) return false;

            IdentifyRequested?.Invoke(this, new TrakHoundAppIdentifyRequest { ConnectionId = connectionId, Seconds = seconds });
            return true;
        }

        /// <summary>
        /// Show each open page's connection Code for a few seconds (every page of the App)
        /// </summary>
        public bool IdentifyApp(string appId, int seconds = TrakHoundAppIdentifyRequest.DefaultSeconds)
        {
            if (string.IsNullOrEmpty(appId)) return false;

            IdentifyRequested?.Invoke(this, new TrakHoundAppIdentifyRequest { AppId = appId, Seconds = seconds });
            return true;
        }

        /// <summary>
        /// Show a Message on the connection's page (not stored : it is lost if the page reloads). Seconds hides it automatically (0 to show it until the user closes it)
        /// </summary>
        public bool SendConnectionMessage(string connectionId, string message, TrakHoundAppMessageSeverity severity = TrakHoundAppMessageSeverity.Information, string from = TrakHoundAppMessageRequest.DefaultFrom, int seconds = 0)
        {
            if (string.IsNullOrEmpty(connectionId) || string.IsNullOrWhiteSpace(message)) return false;

            MessageRequested?.Invoke(this, new TrakHoundAppMessageRequest { ConnectionId = connectionId, Message = TrimMessage(message), Severity = severity, From = TrimFrom(from), Seconds = Math.Clamp(seconds, 0, TrakHoundAppMessageRequest.MaxSeconds) });
            return true;
        }

        /// <summary>
        /// Show a Message on every open page of the App (not stored : pages opened later do not show it). Seconds hides it automatically (0 to show it until the user closes it)
        /// </summary>
        public bool SendAppMessage(string appId, string message, TrakHoundAppMessageSeverity severity = TrakHoundAppMessageSeverity.Information, string from = TrakHoundAppMessageRequest.DefaultFrom, int seconds = 0)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrWhiteSpace(message)) return false;

            MessageRequested?.Invoke(this, new TrakHoundAppMessageRequest { AppId = appId, Message = TrimMessage(message), Severity = severity, From = TrimFrom(from), Seconds = Math.Clamp(seconds, 0, TrakHoundAppMessageRequest.MaxSeconds) });
            return true;
        }

        private static string TrimMessage(string message)
        {
            message = message.Trim();
            return message.Length > TrakHoundAppMessageRequest.MaxLength ? message.Substring(0, TrakHoundAppMessageRequest.MaxLength) : message;
        }

        private static string TrimFrom(string from)
        {
            if (string.IsNullOrWhiteSpace(from)) return TrakHoundAppMessageRequest.DefaultFrom;

            from = from.Trim();
            return from.Length > TrakHoundAppMessageRequest.MaxFromLength ? from.Substring(0, TrakHoundAppMessageRequest.MaxFromLength) : from;
        }

        /// <summary>
        /// Ask every open page of the App to reload when convenient (instead of reloading them immediately with RefreshApp)
        /// </summary>
        public bool NotifyAppReload(string appId)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                AppReloadNotifyRequested?.Invoke(this, appId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ask the Connection's page to reload when convenient (instead of reloading it immediately with RefreshConnection)
        /// </summary>
        public bool NotifyConnectionReload(string connectionId)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                ConnectionReloadNotifyRequested?.Invoke(this, connectionId);
                return true;
            }

            return false;
        }

        public bool RefreshConnection(string connectionId, int delay)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                if (ConnectionRefreshRequested != null) ConnectionRefreshRequested.Invoke(connectionId, delay);

                return true;
            }

            return false;
        }

        public string RewriteUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                url = url.TrimStart('/');
                var basePath = Url.Combine(Url.GetLeftPart(url), _instance.Configuration.BasePath);
                var path = GetBaseRelativePath(basePath, url);
                if (path == ".") path = "/";
                if (!path.StartsWith('/')) path = '/' + path;

                path = RewritePath(path);
                return path;
                //return Url.Combine(basePath, path);
            }

            return null;
        }

        // Path.GetRelativePath compares case-insensitively on Windows and macOS
        private static readonly StringComparison _pathComparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Same result as Url.GetRelativeTo, which runs Path.GetRelativePath (resolving both against the working directory) on every link render.
        /// A plain path under the base is just the base prefix stripped, so that's done directly and anything else uses Url.GetRelativeTo
        /// </summary>
        private static string GetBaseRelativePath(string basePath, string url)
        {
            if (!string.IsNullOrEmpty(basePath) && !string.IsNullOrEmpty(url) && IsPlainRelativePath(basePath) && IsPlainRelativePath(url))
            {
                if (url.Length == basePath.Length && url.Equals(basePath, _pathComparison)) return ".";

                if (url.Length > basePath.Length + 1 && url[basePath.Length] == '/' && url.StartsWith(basePath, _pathComparison))
                {
                    return url.Substring(basePath.Length + 1);
                }
            }

            return Url.GetRelativeTo(basePath, url);
        }

        // True if Path.GetFullPath wouldn't change the path's segments (no '\', ':', empty, "." or ".." segments, trailing separator,
        // or segment ending in '.' or ' ', which Windows trims)
        private static bool IsPlainRelativePath(string path)
        {
            if (path.Contains('\\') || path.Contains(':')) return false;

            var start = 0;
            while (start <= path.Length)
            {
                var end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;

                var length = end - start;
                if (length == 0) return false;
                var last = path[end - 1];
                if (last == '.' || last == ' ') return false; // Includes "." and ".."

                start = end + 1;
            }

            return true;
        }

        private string RewritePath(string path)
        {
            var routeRewrites = _instance.Configuration.RouteRewrites;
            if (routeRewrites.IsNullOrEmpty()) return path;

            foreach (var routeRewrite in routeRewrites)
            {
                var input = path;
                var output = Rewrite(input, routeRewrite.Key, routeRewrite.Value);
                if (!output.StartsWith('/')) output = '/' + output;

                path = output;
            }

            return path;
        }


        // Rewrite Regexes are built once per pattern (the static Regex cache only holds 15) and bounded in case patterns change often
        private const int MaxRewriteRegexes = 100;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _rewriteRegexes = new System.Collections.Concurrent.ConcurrentDictionary<string, Regex>(); // Pattern => Regex

        /// <summary>
        /// Same as Url.Rewrite but with the pattern's Regex cached
        /// </summary>
        private static string Rewrite(string route, string pattern, string replacement)
        {
            if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(pattern))
            {
                var queryParameters = Url.GetQueryParameters(route);
                var fRoute = Url.RemoveQueryParameters(route);

                if (!_rewriteRegexes.TryGetValue(pattern, out var regex))
                {
                    regex = new Regex(pattern);
                    if (_rewriteRegexes.Count >= MaxRewriteRegexes) _rewriteRegexes.Clear();
                    _rewriteRegexes[pattern] = regex;
                }

                var match = regex.Match(fRoute);
                if (match.Success)
                {
                    if (replacement == "/") return "/";

                    var result = replacement ?? "";
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        result = result.Replace("{R:" + i + "}", match.Groups[i].Value);
                    }
                    fRoute = result;
                }

                return Url.AddQueryParameters(fRoute, queryParameters);
            }

            return route;
        }


        private bool IsRouteMatch(string url, string route)
        {
            if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(route)) return true;

            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(route))
            {
                // The Regex is cached per Route (instead of caching each URL's result, which grows with every URL requested)
                var urlRegex = _routeRegexes.GetOrAdd(route, o => new Regex(CreateRouteRegexPattern(o)));
                return urlRegex.IsMatch(url) && url.AsSpan().Count('/') == route.AsSpan().Count('/');
            }

            return false;
        }

        private static string CreateRouteRegexPattern(string route)
        {
            var pattern = route;

            if (!string.IsNullOrEmpty(route))
            {
                if (_routeParameterRegex.IsMatch(route))
                {
                    var matches = _routeParameterRegex.Matches(route);
                    if (!matches.IsNullOrEmpty())
                    {
                        foreach (Match match in matches)
                        {
                            if (match.Groups?.Count > 1)
                            {
                                var matchText = match.Groups[1].ToString();

                                pattern = pattern.Replace(matchText, ".*");
                            }
                        }
                    }
                }
            }

            return $"^{pattern}$";
        }

        public Type GetModuleType(string packageId, string packageVersion = null)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                if (!string.IsNullOrEmpty(packageVersion))
                {
                    var key = $"{packageId}:{packageVersion}";
                    lock (_lock) return _moduleInfos.GetValueOrDefault(key).Type;
                }
                else
                {
                    lock (_lock)
                    {
                        var values = _moduleInfos.Values;
                        return values.Where(o => o.PackageId == packageId)?.OrderByDescending(o => o.PackageVersion, VersionComparer.Default).FirstOrDefault().Type;
                    }
                }
            }


            return null;
        }

        public IEnumerable<string> GetAppStylesheets(string appId)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                lock (_lock)
                {
                    return _packageStylesheets.Get(appId);
                }
            }

            return null;
        }

        public IEnumerable<string> GetAppScripts(string appId)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                lock (_lock)
                {
                    return _packageScripts.Get(appId);
                }
            }

            return null;
        }


        public IEnumerable<TrakHoundAppMetrics> GetMetrics()
        {
            var metrics = Metrics.GetMetrics();
            if (metrics != null) foreach (var appMetrics in metrics) SetCpuUsage(appMetrics);
            return metrics;
        }

        public TrakHoundAppMetrics GetMetrics(string appId)
        {
            var metrics = Metrics.GetMetrics(appId);
            SetCpuUsage(metrics);
            return metrics;
        }

        // Measured by TrakHoundAppCpuCircuitHandler
        private static void SetCpuUsage(TrakHoundAppMetrics metrics)
        {
            if (metrics == null) return;

            var monitor = TrakHoundModuleCpuMonitors.Default.FindMonitor(TrakHound.Instances.TrakHoundCpuModuleTypes.App, metrics.AppId);
            metrics.CpuUsage = Math.Round(monitor?.Usage ?? 0, 2);
            metrics.CpuTime = Math.Round(monitor?.TotalProcessorTime.TotalMilliseconds ?? 0);
        }

        public IEnumerable<TrakHoundAppConnectionMetrics> GetConnectionMetrics(string appId = null) => Metrics.GetConnectionMetrics(appId);


        private void UpdateDelayElapsed(object sender, EventArgs e)
        {
            // Exceptions in Timer callbacks are swallowed, so log them here
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"App Load Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        private void Update(object sender, ITrakHoundModule module)
        {
            _delayLoadEvent.Set();
        }

        public void Load()
        {
            // Only one Load (or ReloadConfiguration) at a time
            lock (_loadLock)
            {
                // The Injection Service Manager reloads itself when Configurations or Packages change, so it is only created once.
                // (Recreating it would remove the Services in use by open App connections)
                if (_injectionServiceManager == null)
                {
                    _injectionServiceManager = new TrakHoundAppInjectionServiceManager(_configurationProfile, _packageManager, _moduleProvider, _moduleContext, _clientProvider, _volumeProvider, _variableProvider, _logProvider, _getServiceProvider);
                }

                // Load runs again ~5s after every Package and App Configuration notification (ex. for every installed Package after Start),
                // so skip it (keeping the current Route Manager and files) when no App Configuration or Package has changed
                var loadState = GetLoadState();
                if (_routeManager != null && loadState == _loadedState) return;

                _loadFailed = false;

                // Build the new Route Manager completely before replacing the current one,
                // so that App Routes remain valid while Loading
                var routeManager = new TrakHoundAppPageRouteManager<ComponentBase>(_instance.Configuration.BasePath, _instance, _configurationProfile, _moduleProvider, _packageCategory, _moduleContext, _clientProvider, _volumeProvider, _logProvider);

                LoadInformation();
                LoadPages(routeManager);
                routeManager.Initialise();

                // Copy App files before the new Routes are used
                LoadStylesheets();
                LoadScripts();
                LoadStaticAssets();

                // The previous Route Manager is not Disposed : open App pages may still hold it (from their RouteData)
                // and use it to Match navigation. It has no external subscriptions, so it is collected once unused.
                _routeManager = routeManager;

                _loadedState = !_loadFailed ? loadState : null;
            }
        }

        /// <summary>
        /// Identifies everything a Load reads : the Instance's routing settings, and each App Configuration with the Module (Package build) it uses
        /// </summary>
        private string GetLoadState()
        {
            var state = new System.Text.StringBuilder();

            var instanceConfiguration = _instance?.Configuration;
            if (instanceConfiguration != null)
            {
                state.Append(instanceConfiguration.BasePath).Append('|').Append(instanceConfiguration.AdminInterfaceEnabled);
                if (!instanceConfiguration.RouteRewrites.IsNullOrEmpty())
                {
                    foreach (var routeRewrite in instanceConfiguration.RouteRewrites) state.Append('|').Append(routeRewrite.Key).Append('=').Append(routeRewrite.Value);
                }
            }

            var configurations = _configurationProfile.Get<ITrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);

                    // With the Router it resolves to, so a change to its Group's Router reloads it
                    state.Append('\n').Append(configuration.Id).Append('|').Append(TrakHoundGroups.GetLoadHash(_configurationProfile, configuration.Hash, configuration.RouterId, configuration.GroupId));
                    if (module != null)
                    {
                        // The Module instance too, in case it is reloaded without the Package changing (the Routes hold its Types)
                        state.Append('|').Append(module.Package?.Hash).Append('|').Append(module.Package?.Location).Append('|').Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(module));
                    }
                }
            }

            return state.ToString();
        }


        private void LoadInformation()
        {
            var foundConfigurationIds = new List<string>();

            var configurations = _configurationProfile.Get<ITrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations)
                {
                    var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                    if (module != null)
                    {
                        // Get the Installed Hash (to check if the configuration has changed)
                        string installedConfigurationHash;
                        string installedModuleHash;
                        lock (_lock)
                        {
                            installedConfigurationHash = _installedConfigurationHashes.GetValueOrDefault(configuration.Id);
                            installedModuleHash = _installedModuleHashes.GetValueOrDefault(configuration.Id);
                        }

                        if (configuration.Hash != installedConfigurationHash || module.Package.Hash != installedModuleHash)
                        {
                            // Configure each App separately so one failing App doesn't stop the others from Loading
                            try
                            {
                                ConfigureRoute(configuration, module);
                                SetInstalledHashes(configuration, module); // Only once Configured, so a failed App is retried on the next Load
                            }
                            catch (Exception ex)
                            {
                                _loadFailed = true;
                                _logger?.LogError($"App Load Error : {configuration.Id} : {configuration.PackageId} v{configuration.PackageVersion} : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
                            }
                        }

                        foundConfigurationIds.Add(configuration.Id);
                    }
                    else
                    {
                        _loadFailed = true;
                        _logger?.LogError($"App Load Error : Module Not Found : {configuration.Id} : Package ID = {configuration.PackageId} v{configuration.PackageVersion}");
                    }
                }
            }

            // Remove unused
            var removedApps = new List<TrakHoundAppInformation>();
            lock (_lock)
            {
                foreach (var appInformation in _appInformations.Values.ToList())
                {
                    if (!foundConfigurationIds.Contains(appInformation.Id))
                    {
                        _appInformations.Remove(appInformation.Id);
                        _appPackages.Remove(appInformation.Id);

                        // Remove the Installed Hashes so that the App is Configured again if it is added back unchanged
                        _installedConfigurationHashes.Remove(appInformation.Id);
                        _installedModuleHashes.Remove(appInformation.Id);

                        removedApps.Add(appInformation);
                    }
                }
            }

            foreach (var removedApp in removedApps)
            {
                UnloadApp(removedApp);
            }
        }

        private void UnloadApp(TrakHoundAppInformation appInformation)
        {
            // Stop including the App's Stylesheets and Scripts in pages
            _packageStylesheets.Remove(appInformation.Id);
            _packageScripts.Remove(appInformation.Id);

            // Remove Security Resources
            if (_instance != null && !appInformation.Pages.IsNullOrEmpty())
            {
                foreach (var pageInformation in appInformation.Pages)
                {
                    _instance.SecurityManager.RemoveResource(GetSecurityResourceId(appInformation, pageInformation));
                }
            }

            AppRemoved?.Invoke(this, appInformation.Id);
        }

        private static string GetSecurityResourceId(TrakHoundAppInformation appInformation, TrakHoundAppPageInformation pageInformation)
        {
            return $"app:{appInformation.Id}:{pageInformation.Id}";
        }

        /// <summary>
        /// Gets the Security Resource ID of an App Page (the same ID that is registered when the App is loaded)
        /// </summary>
        public static string GetSecurityResourceId(string appId, Type pageType)
        {
            return $"app:{appId}:{GetPageId(pageType)}";
        }

        // The Page's Class Name, so that a Security Profile can name a Page (ex. app:{appId}:MyApp.Pages.SignIn) or a Namespace of Pages (ex. app:{appId}:MyApp.Pages.Public.*)
        private static string GetPageId(Type pageType) => pageType?.FullName;

        private void SetInstalledHashes(ITrakHoundAppConfiguration configuration, ITrakHoundModule module)
        {
            lock (_lock)
            {
                _installedConfigurationHashes[configuration.Id] = configuration.Hash;
                _installedModuleHashes[configuration.Id] = module.Package.Hash;
            }
        }

        private List<KeyValuePair<string, TrakHoundPackage>> GetAppPackages()
        {
            lock (_lock) return _appPackages.ToList();
        }

        private void ConfigureRoute(ITrakHoundAppConfiguration configuration, ITrakHoundModule module)
        {
            if (configuration != null && module != null && !module.ModuleTypes.IsNullOrEmpty())
            {
                var route = configuration.Route;
                if (string.IsNullOrEmpty(route)) route = "/";
                if (!route.StartsWith('/')) route = '/' + route;

                // The Theme shown until a user picks one (ex. ".defaultTheme": "dark" in trakhound.package.json)
                if (Enum.TryParse<TrakHoundThemes>(module.Package.GetMetadata(_defaultThemeMetadataName), true, out var defaultTheme))
                {
                    TrakHound.Blazor.Services.TrakHoundThemeService.SetDefaultTheme(TrakHound.Blazor.Services.TrakHoundThemeService.GetAppKey(configuration.Id), defaultTheme);
                }

                // Read the Package ReadMe (if exists)
                var packageReadMe = _packageManager.GetReadMe(module.Package.Category, module.Package.Id, module.Package.Version);

                var pageInfos = new List<TrakHoundAppPageInformation>();
                foreach (var moduleType in module.ModuleTypes) // Page Types
                {
                    var pageParameterInfos = new List<TrakHoundAppPageParameterInformation>();

                    var pageProperties = moduleType.GetProperties();
                    if (!pageProperties.IsNullOrEmpty())
                    {
                        foreach (var pageProperty in pageProperties)
                        {
                            var parameterAttribute = pageProperty.GetCustomAttribute<ParameterAttribute>();
                            if (parameterAttribute != null)
                            {
                                var pageParameterInfo = new TrakHoundAppPageParameterInformation();
                                pageParameterInfo.Name = pageProperty.Name;
                                pageParameterInfo.DataType = pageProperty.PropertyType.ToString();
                                pageParameterInfos.Add(pageParameterInfo);
                            }
                        }
                    }

                    var routeAttributes = moduleType.GetCustomAttributes<RouteAttribute>();
                    if (!routeAttributes.IsNullOrEmpty())
                    {
                        foreach (var routeAttribute in routeAttributes)
                        {
                            if (!string.IsNullOrEmpty(routeAttribute.Template))
                            {
                                var pageInfo = new TrakHoundAppPageInformation();
                                pageInfo.Id = GetPageId(moduleType);
                                pageInfo.Route = routeAttribute.Template;
                                pageInfo.Parameters = pageParameterInfos;
                                pageInfos.Add(pageInfo);
                            }
                        }
                    }
                }


                // Create Information
                var appInformation = new TrakHoundAppInformation();
                appInformation.Id = configuration.Id;
                appInformation.Name = configuration.Name;
                appInformation.Route = route;
                appInformation.PackageId = configuration.PackageId;
                appInformation.GroupId = configuration.GroupId;

                appInformation.PackageVersion = module.Package.Version;
                appInformation.PackageBuildDate = module.Package.BuildDate;
                appInformation.PackageHash = module.Package.Hash;
                appInformation.PackageIcon = module.Package.GetMetadata(TrakHoundPackage.ImageName);
                appInformation.PackageReadMe = packageReadMe;

                appInformation.TrakHoundVersion = module.Package.GetMetadata(TrakHoundPackage.TrakHoundVersionName);

                appInformation.Repository = module.Package.GetMetadata(TrakHoundPackage.RepositoryName);
                appInformation.RepositoryBranch = module.Package.GetMetadata(TrakHoundPackage.RepositoryBranchName);
                appInformation.RepositoryDirectory = module.Package.GetMetadata(TrakHoundPackage.RepositoryDirectoryName);
                appInformation.RepositoryCommit = module.Package.GetMetadata(TrakHoundPackage.RepositoryCommitName);

                appInformation.Pages = pageInfos;

                lock (_lock)
                {
                    _appInformations.Remove(appInformation.Id);
                    _appInformations.Add(appInformation.Id, appInformation);

                    _appPackages.Remove(appInformation.Id);
                    _appPackages.Add(appInformation.Id, module.Package);
                }

                if (_instance != null)
                {
                    // Add Security Resources
                    foreach (var pageInformation in appInformation.Pages)
                    {
                        var resourceId = GetSecurityResourceId(appInformation, pageInformation);

                        _instance.SecurityManager.AddResource(Security.TrakHoundIdentityResourceType.App, resourceId);
                    }
                }

                if (AppAdded != null) AppAdded.Invoke(this, appInformation);
            }
        }


        private void LoadPages(TrakHoundAppPageRouteManager routeManager)
        {
            var appConfigurations = _configurationProfile.Get<ITrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
            if (!appConfigurations.IsNullOrEmpty())
            {
                foreach (var appConfiguration in appConfigurations)
                {
                    routeManager.AddApp(appConfiguration);
                }
            }
        }


        private void LoadStylesheets()
        {
            var stylesheets = new List<StylesheetInfo>();
            var packageHashes = new Dictionary<string, string>(); // Package Directory => Package Hash

            foreach (var appPackage in GetAppPackages())
            {
                var appId = appPackage.Key;
                var package = appPackage.Value;

                if (!string.IsNullOrEmpty(appId))
                {
                    var stylesheet = new StylesheetInfo();
                    stylesheet.AppId = appId;
                    stylesheet.PackageDirectory = package.Location;
                    stylesheet.PackageId = package.Id;
                    stylesheet.PackageVersion = package.Version;
                    stylesheets.Add(stylesheet);

                    if (!string.IsNullOrEmpty(package.Location)) packageHashes[package.Location] = package.Hash;
                }
            }

            UpdateStylesheets(stylesheets, packageHashes);
        }

        private void LoadScripts()
        {
            var stylesheets = new List<StylesheetInfo>();
            var packageHashes = new Dictionary<string, string>(); // Package Directory => Package Hash

            foreach (var appPackage in GetAppPackages())
            {
                var appId = appPackage.Key;
                var package = appPackage.Value;

                if (!string.IsNullOrEmpty(appId))
                {
                    var stylesheet = new StylesheetInfo();
                    stylesheet.AppId = appId;
                    stylesheet.PackageDirectory = package.Location;
                    stylesheet.PackageId = package.Id;
                    stylesheet.PackageVersion = package.Version;
                    stylesheets.Add(stylesheet);

                    if (!string.IsNullOrEmpty(package.Location)) packageHashes[package.Location] = package.Hash;
                }
            }

            UpdateScripts(stylesheets, packageHashes);
        }

        /// <summary>
        /// The key that records that a Package's files of a type have been copied to a Destination Directory
        /// </summary>
        private static string GetCopiedKey(string fileType, string destinationDirectory) => $"{fileType}:{destinationDirectory}";

        /// <summary>
        /// The Hash of the Package (and its Location) to compare with the copied files. Null (always copy) if the Hash isn't known
        /// </summary>
        private static string GetCopiedValue(IReadOnlyDictionary<string, string> packageHashes, string packageDirectory)
        {
            if (packageHashes != null && !string.IsNullOrEmpty(packageDirectory) && packageHashes.TryGetValue(packageDirectory, out var packageHash) && !string.IsNullOrEmpty(packageHash))
            {
                return $"{packageHash}|{packageDirectory}";
            }

            return null;
        }

        private bool IsCopied(string copiedKey, string copiedValue)
        {
            if (copiedValue == null) return false;

            lock (_lock) return _copiedPackages.GetValueOrDefault(copiedKey) == copiedValue;
        }

        private void SetCopied(string copiedKey, string copiedValue, bool failed)
        {
            lock (_lock)
            {
                // A Package with a file that failed to copy isn't recorded, so it is copied again on the next Load
                if (copiedValue != null && !failed) _copiedPackages[copiedKey] = copiedValue;
                else _copiedPackages.Remove(copiedKey);
            }

            if (failed) _loadFailed = true;
        }

        /// <summary>
        /// Copy a file unless the Destination already has the same size and last write time (the last write time is copied too, so that the check works on every OS).
        /// Overwriting a file that is being read (ex. served to a browser) throws on Windows, so unchanged files are left alone
        /// </summary>
        private static void CopyIfChanged(string sourceFile, string destinationFile)
        {
            var source = new FileInfo(sourceFile);
            var destination = new FileInfo(destinationFile);
            if (destination.Exists && destination.Length == source.Length && destination.LastWriteTimeUtc == source.LastWriteTimeUtc) return;

            File.Copy(sourceFile, destinationFile, true);
            File.SetLastWriteTimeUtc(destinationFile, source.LastWriteTimeUtc);
        }

        /// <summary>
        /// Get the files in the Package's "dist/wwwroot" directory (the only directory that is served).
        /// Files outside of it would otherwise produce a relative path (ex. "..\..\file.css") that escapes the Package's served directory.
        /// </summary>
        private static string[] GetPackageWwwRootFiles(string packageDirectory, string searchPattern)
        {
            var wwwRootPath = Path.Combine(packageDirectory, "dist", "wwwroot");
            if (Directory.Exists(wwwRootPath))
            {
                return Directory.GetFiles(wwwRootPath, searchPattern, SearchOption.AllDirectories);
            }

            return Array.Empty<string>();
        }

        private static bool IsWithinDirectory(string path, string directory)
        {
            var fullPath = Path.GetFullPath(path);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        public void UpdateStylesheets(IEnumerable<StylesheetInfo> stylesheets, IReadOnlyDictionary<string, string> packageHashes = null)
        {
            if (!stylesheets.IsNullOrEmpty())
            {
                foreach (var stylesheet in stylesheets)
                {
                    _packageStylesheets.Remove(stylesheet.AppId);
                    //lock (_lock) _packageStylesheets.Remove(stylesheet.AppId);

                    if (!string.IsNullOrEmpty(stylesheet.PackageDirectory))
                    {
                        var wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_temp", "wwwroot");
                        var appRoot = Path.Combine(wwwRoot, _packagesDirectory);
                        var destinationDirectory = Path.Combine(appRoot, stylesheet.PackageId, stylesheet.PackageVersion);

                        // The rewritten Stylesheets only change with the Package (the Base Path is fixed for the Instance's lifetime)
                        var copiedKey = GetCopiedKey("css", destinationDirectory);
                        var copiedValue = GetCopiedValue(packageHashes, stylesheet.PackageDirectory);
                        var unchanged = IsCopied(copiedKey, copiedValue);
                        var failed = false;

                        try
                        {
                            var stylesheetFiles = GetPackageWwwRootFiles(stylesheet.PackageDirectory, "*.css");
                            if (!stylesheetFiles.IsNullOrEmpty())
                            {
                                var packageDistPath = Path.Combine(stylesheet.PackageDirectory, "dist");
                                var packageWwwRootPath = Path.Combine(packageDistPath, "wwwroot");

                                foreach (var stylesheetFile in stylesheetFiles)
                                {
                                    if (!stylesheetFile.Contains("TrakHound.Blazor"))
                                    {
                                        if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

                                        var relativeSourcePath = Path.GetRelativePath(packageWwwRootPath, stylesheetFile);
                                        var destinationFile = Path.Combine(destinationDirectory, relativeSourcePath);
                                        if (!IsWithinDirectory(destinationFile, destinationDirectory)) continue;

                                        var stylesheetPath = Path.GetRelativePath(appRoot, destinationFile);

                                        if (!unchanged || !File.Exists(destinationFile))
                                        {
                                            // Each file separately so that one locked file doesn't stop the rest of the Package
                                            try
                                            {
                                                var newLines = new List<string>();
                                                var contentLines = File.ReadAllLines(stylesheetFile);
                                                if (contentLines != null)
                                                {
                                                    var regex = new Regex("^@import '(.*)';$");

                                                    foreach (var contentLine in contentLines)
                                                    {
                                                        var match = regex.Match(contentLine);
                                                        if (match.Success)
                                                        {
                                                            var originalPath = match.Groups[1].Value;

                                                            if (!originalPath.Contains("TrakHound.Blazor"))
                                                            {
                                                                var newPath = Path.Combine(_packagesDirectory, stylesheet.PackageId, stylesheet.PackageVersion, originalPath);
                                                                newPath = newPath.Replace('\\', '/');

                                                                if (!string.IsNullOrEmpty(_instance.Configuration.BasePath))
                                                                {
                                                                    newPath = Url.Combine(_instance.Configuration.BasePath, newPath);
                                                                }

                                                                var newLine = $"@import '/{newPath}';";
                                                                newLines.Add(newLine);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            newLines.Add(contentLine);
                                                        }
                                                    }
                                                }

                                                var destinationFileDirectory = Path.GetDirectoryName(destinationFile);
                                                if (!Directory.Exists(destinationFileDirectory)) Directory.CreateDirectory(destinationFileDirectory);

                                                if (File.Exists(destinationFile)) File.Delete(destinationFile);
                                                File.WriteAllLines(destinationFile, newLines);
                                            }
                                            catch (Exception ex)
                                            {
                                                failed = true;
                                                _logger?.LogError($"Error Copying App Stylesheet : {stylesheet.PackageId} v{stylesheet.PackageVersion} : {relativeSourcePath} : {ex.Message}");
                                            }
                                        }

                                        if (!_packageStylesheets.Contains(stylesheet.AppId, stylesheetPath))
                                        {
                                            _packageStylesheets.Add(stylesheet.AppId, stylesheetPath);
                                        }

                                        //lock (_lock) _packageStylesheets.Add(stylesheet.AppId, stylesheetPath);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            _logger?.LogError($"Error Copying App Stylesheets : {stylesheet.PackageId} v{stylesheet.PackageVersion} : {ex.Message}");
                        }

                        SetCopied(copiedKey, copiedValue, failed);
                    }
                }
            }
        }

        public void UpdateScripts(IEnumerable<StylesheetInfo> stylesheets, IReadOnlyDictionary<string, string> packageHashes = null)
        {
            if (!stylesheets.IsNullOrEmpty())
            {
                foreach (var stylesheet in stylesheets)
                {
                    lock (_lock) _packageScripts.Remove(stylesheet.AppId);

                    if (!string.IsNullOrEmpty(stylesheet.PackageDirectory))
                    {
                        var wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_temp", "wwwroot");
                        var appRoot = Path.Combine(wwwRoot, _packagesDirectory);
                        var destinationDirectory = Path.Combine(appRoot, stylesheet.PackageId, stylesheet.PackageVersion);

                        var copiedKey = GetCopiedKey("js", destinationDirectory);
                        var copiedValue = GetCopiedValue(packageHashes, stylesheet.PackageDirectory);
                        var unchanged = IsCopied(copiedKey, copiedValue);
                        var failed = false;

                        try
                        {
                            var scriptFiles = GetPackageWwwRootFiles(stylesheet.PackageDirectory, "*.js");
                            if (!scriptFiles.IsNullOrEmpty())
                            {
                                var packageDistPath = Path.Combine(stylesheet.PackageDirectory, "dist");
                                var packageWwwRootPath = Path.Combine(packageDistPath, "wwwroot");

                                foreach (var scriptFile in scriptFiles)
                                {
                                    if (!scriptFile.Contains("TrakHound.Blazor"))
                                    {
                                        if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

                                        var relativeSourcePath = Path.GetRelativePath(packageWwwRootPath, scriptFile);
                                        var destinationFile = Path.Combine(destinationDirectory, relativeSourcePath);
                                        if (!IsWithinDirectory(destinationFile, destinationDirectory)) continue;

                                        var scriptPath = Path.GetRelativePath(appRoot, destinationFile);

                                        if (!unchanged || !File.Exists(destinationFile))
                                        {
                                            // Each file separately so that one locked file doesn't stop the rest of the Package
                                            try
                                            {
                                                var destinationFileDirectory = Path.GetDirectoryName(destinationFile);
                                                if (!Directory.Exists(destinationFileDirectory)) Directory.CreateDirectory(destinationFileDirectory);

                                                CopyIfChanged(scriptFile, destinationFile);
                                            }
                                            catch (Exception ex)
                                            {
                                                failed = true;
                                                _logger?.LogError($"Error Copying App Script : {stylesheet.PackageId} v{stylesheet.PackageVersion} : {relativeSourcePath} : {ex.Message}");
                                            }
                                        }

                                        lock (_lock) _packageScripts.Add(stylesheet.AppId, scriptPath);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            _logger?.LogError($"Error Copying App Scripts : {stylesheet.PackageId} v{stylesheet.PackageVersion} : {ex.Message}");
                        }

                        SetCopied(copiedKey, copiedValue, failed);
                    }
                }
            }
        }

        private void LoadStaticAssets()
        {
            // Only the Packages used by App Configurations : App pages reference their own Package's files (Route.BaseLocation).
            // Files already copied for a Package that is no longer used are left in place for pages that are still open
            var assets = new List<StaticAssetInfo>();
            var packageHashes = new Dictionary<string, string>(); // Package Directory => Package Hash

            foreach (var appPackage in GetAppPackages())
            {
                var package = appPackage.Value;
                if (package != null && !string.IsNullOrEmpty(package.Location))
                {
                    if (!assets.Any(o => o.PackageDirectory == package.Location))
                    {
                        var asset = new StaticAssetInfo();
                        asset.PackageDirectory = package.Location;
                        asset.PackageId = package.Id;
                        asset.PackageVersion = package.Version;
                        assets.Add(asset);

                        packageHashes[package.Location] = package.Hash;
                    }
                }
            }

            UpdateStaticAssets(assets, packageHashes);
        }

        public void UpdateStaticAssets(IEnumerable<StaticAssetInfo> assets, IReadOnlyDictionary<string, string> packageHashes = null)
        {
            if (!assets.IsNullOrEmpty())
            {
                foreach (var asset in assets)
                {
                    if (!string.IsNullOrEmpty(asset.PackageDirectory))
                    {
                        var sourceRoot = Path.Combine(asset.PackageDirectory, "dist", "wwwroot");
                        var wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_temp", "wwwroot");
                        var appRoot = Path.Combine(wwwRoot, _packagesDirectory);
                        var destinationDirectory = Path.Combine(appRoot, asset.PackageId, asset.PackageVersion);

                        // Nothing to do if this build of the Package has already been copied
                        var copiedKey = GetCopiedKey("assets", destinationDirectory);
                        var copiedValue = GetCopiedValue(packageHashes, asset.PackageDirectory);
                        if (IsCopied(copiedKey, copiedValue) && Directory.Exists(destinationDirectory)) continue;

                        var failed = false;

                        try
                        {
                            // A Package without a "dist/wwwroot" directory has no Static Assets (not an error)
                            var assetFiles = Directory.Exists(sourceRoot) ? Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories) : Array.Empty<string>();
                            if (!assetFiles.IsNullOrEmpty())
                            {
                                foreach (var assetFile in assetFiles)
                                {
                                    if (Path.GetExtension(assetFile) != ".css" && Path.GetExtension(assetFile) != ".js")
                                    {
                                        if (!assetFile.Contains("TrakHound.Blazor"))
                                        {
                                            // Each file separately so that one locked file doesn't stop the rest of the Package
                                            try
                                            {
                                                if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

                                                var destinationFile = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceRoot, assetFile));
                                                var destinationSubDirectory = Path.GetDirectoryName(destinationFile);
                                                if (!Directory.Exists(destinationSubDirectory)) Directory.CreateDirectory(destinationSubDirectory);

                                                CopyIfChanged(assetFile, destinationFile);
                                            }
                                            catch (Exception ex)
                                            {
                                                failed = true;
                                                _logger?.LogError($"Error Copying App Static Asset : {asset.PackageId} v{asset.PackageVersion} : {Path.GetRelativePath(sourceRoot, assetFile)} : {ex.Message}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            _logger?.LogError($"Error Copying App Static Assets : {asset.PackageId} v{asset.PackageVersion} : {ex.Message}");
                        }

                        SetCopied(copiedKey, copiedValue, failed);
                    }
                }
            }
        }

        public void ClearFiles()
        {
            var wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_temp", "wwwroot");
            if (Directory.Exists(wwwRoot))    
            {
                try
                {
                    Directory.Delete(wwwRoot, true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Error Clearing App Files : {wwwRoot} : {ex.Message}");
                }
            }
        }
    }
}
