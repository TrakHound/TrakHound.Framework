// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Instance.Api;
using TrakHound.Instance.Http;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;
using TrakHound.Zones;

namespace TrakHound.Api
{
    public class TrakHoundApiProvider : ITrakHoundApiProvider
    {
        private const string _packagesDirectory = "_packages";
        private const string _packageCategory = "api";
        private const int _maxParallelLoads = 4;
        private const int _maxCatalogDocuments = 256;

        private const string _routeParameterRegexPattern = @"(\{(.*?)\})";
        private static readonly Regex _routeParameterRegex = new Regex(_routeParameterRegexPattern, RegexOptions.Compiled);
        private static readonly ApiRouteComparer _routeComparer = new ApiRouteComparer();

        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly ITrakHoundLogger _logger;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly TrakHoundApiConnectionManager _connectionManager;
        private readonly TrakHoundApiStreamManager _streamManager;
        private readonly string _baseUrl;

        // Replaced as a whole (under _lock) and read without the lock, since it is read several times for every Request
        private volatile ApiRouteConfiguration[] _routes;
        private readonly ConcurrentDictionary<string, Regex> _routeRegexes = new ConcurrentDictionary<string, Regex>(); // Route => Route Regex
        private readonly ConcurrentDictionary<string, (string Key, string Document)> _openApiDocuments = new ConcurrentDictionary<string, (string Key, string Document)>(); // ApiId => (Route Key, OpenAPI Document)
        private readonly ConcurrentDictionary<string, string> _catalogDocuments = new ConcurrentDictionary<string, string>(); // ApiId|Route Key|Access|Server URL|Header => Catalog OpenAPI Document
        private readonly ConditionalWeakTable<TrakHoundApiController, MethodRoute[]> _controllerRoutes = new ConditionalWeakTable<TrakHoundApiController, MethodRoute[]>();
        private readonly ConcurrentDictionary<MethodInfo, MethodParameters> _cachedParameters = new ConcurrentDictionary<MethodInfo, MethodParameters>();

        private readonly Dictionary<string, string> _installedConfigurationHashes = new Dictionary<string, string>(); // Configuration.Id => Configuration.Hash and the Router it resolves to
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(); // Configuration.Id => Module.Hash
        private readonly ListDictionary<string, ITrakHoundStream<TrakHoundApiResponse>> _consumers = new ListDictionary<string, ITrakHoundStream<TrakHoundApiResponse>>(); // ApiId => Consumers
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(2000);
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();


        public TrakHoundApiConnectionManager ConnectionManager => _connectionManager;

        public event EventHandler<TrakHoundApiInformation> ApiAdded;

        public event EventHandler<string> ApiRemoved;

        public event EventHandler<Exception> ApiLoadError;

        public event TrakHoundApiLogHandler ApiLogUpdated;


        public TrakHoundApiProvider(
            ITrakHoundInstance instance,
            ITrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundClientProvider clientProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            ITrakHoundLogProvider logProvider,
            TrakHoundPackageManager packageManager,
            TrakHoundApiConnectionManager connectionManager,
            TrakHoundApiStreamManager streamManager
            )
        {
            _instance = instance;
            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;
            _logProvider = logProvider;
            _logger = logProvider.GetLoggerByName("api");
            _connectionManager = connectionManager;
            _streamManager = streamManager;

            // Set Base URL (HTTP Address)
            if (_instance != null)
            {
                _baseUrl = _instance.Information.GetInterface("HTTP")?.GetBaseUrl();
            }

            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved += ConfigurationUpdated;

            _packageManager = packageManager;
            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;

            _moduleManager = moduleProvider.Get<ITrakHoundApiController>(_packageCategory);

            _delayLoadEvent.Elapsed += LoadDelayElapsed;
        }

        public void Dispose()
        {
            // The Configuration Profile and Package Manager outlive this Provider (a new one is created each time the Instance starts)
            _configurationProfile.ConfigurationAdded -= ConfigurationUpdated;
            _configurationProfile.ConfigurationRemoved -= ConfigurationUpdated;
            _packageManager.PackageAdded -= PackageAdded;
            _packageManager.PackageRemoved -= PackageRemoved;

            _delayLoadEvent.Elapsed -= LoadDelayElapsed;
            _delayLoadEvent.Dispose();

            ApiRouteConfiguration[] routes;

            lock (_lock)
            {
                var consumers = _consumers.Values;
                if (!consumers.IsNullOrEmpty())
                {
                    foreach (var consumer in consumers.ToList())
                    {
                        _streamManager?.SetCloseReason(consumer.Id, TrakHoundApiStreamCloseReasons.Stopped);
                        consumer.Dispose();
                    }
                }

                routes = _routes;
                _routes = null;
                _installedConfigurationHashes.Clear();
                _installedModuleHashes.Clear();
            }

            if (!routes.IsNullOrEmpty())
            {
                foreach (var route in routes) DisposeControllers(route.Controllers);
            }

            _loadLock.Dispose();
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


        private void ConfigurationUpdated(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration != null && configuration.Category == TrakHoundApiConfiguration.ConfigurationCategory)
            {
                _delayLoadEvent.Set();
            }
            else if (configuration != null && configuration.Category == TrakHoundGroupConfiguration.ConfigurationCategory)
            {
                // Reload the Apis whose Router came from the Group (unchanged Apis are skipped)
                _delayLoadEvent.Set();
            }
        }


        /// <summary>
        /// Whether a Request route is for an Api (and should be handled by the Api instead of the Instance or an App)
        /// </summary>
        public bool IsRouteValid(string route)
        {
            if (route != null)
            {
                if (route.Length == 0) route = "/";

                // The Instance's own routes (ex. /_admin, /_blazor, the System API) are never Api routes, even for an Api at "/"
                if (TrakHoundInstanceRoutes.IsReservedRoute("/" + Url.RemoveQueryParameters(route).TrimStart('/'))) return false;

                var path = RewriteRoute(route);
                if (TryGetRouteConfiguration(path, out var routeConfiguration))
                {
                    // An Api at the root only handles Requests for its Endpoints, so that Apps can be served at the other routes
                    if (IsRootRoute(routeConfiguration.Route)) return IsEndpointRoute(routeConfiguration, path);

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the Api that matches a Request route is served over HTTP (it is still available within the Instance when disabled)
        /// </summary>
        public bool IsHttpEnabled(string route)
        {
            if (route != null && TryGetRouteConfiguration(RewriteRoute(route), out var routeConfiguration))
            {
                return routeConfiguration.Configuration?.HttpEnabled ?? true;
            }

            return false;
        }

        private static bool IsRootRoute(string route) => string.IsNullOrEmpty(route?.Trim('/'));

        /// <summary>
        /// Whether the route matches one of the Api's Endpoints (any Method)
        /// </summary>
        private bool IsEndpointRoute(ApiRouteConfiguration routeConfiguration, string route)
        {
            var endpointRoutes = routeConfiguration.EndpointRoutes;
            if (endpointRoutes.IsNullOrEmpty()) return false;

            var matchRoute = "/" + Url.RemoveQueryParameters(route).TrimStart('/');

            foreach (var endpointRoute in endpointRoutes)
            {
                if (IsRouteMatch(matchRoute, endpointRoute)) return true;
            }

            return false;
        }

        private static string[] CreateEndpointRoutes(TrakHoundApiInformation information)
        {
            var endpointRoutes = new List<string>();

            if (information?.Controllers != null)
            {
                foreach (var controllerInformation in information.Controllers)
                {
                    if (controllerInformation.EndPoints == null) continue;

                    foreach (var endpointInformation in controllerInformation.EndPoints)
                    {
                        var endpointRoute = Url.Combine(information.Route, controllerInformation.Route, endpointInformation.Route);
                        if (!endpointRoute.StartsWith("/")) endpointRoute = '/' + endpointRoute;
                        endpointRoutes.Add(endpointRoute);
                    }
                }
            }

            return endpointRoutes.ToArray();
        }

        /// <summary>
        /// Gets the Endpoint that a Request (or Stream) for the Route is dispatched to, resolved the same way as HandleRequest and HandleStream
        /// (Route Rewrites, the most specific Api Route, then the most specific Endpoint Route), so that the Endpoint that is authorized is
        /// always the one that runs. Stream Endpoints are matched regardless of the Method. Null if no Endpoint matches
        /// </summary>
        public TrakHoundApiEndpointInformation GetEndpointInformation(TrakHoundApiEndpointType endpointType, TrakHoundApiMethod method, string route)
        {
            var requestRoute = RewriteRoute(route);
            if (string.IsNullOrEmpty(requestRoute)) return null;

            if (!TryGetRouteConfiguration(requestRoute, out var routeConfiguration) || routeConfiguration.Controllers.IsNullOrEmpty()) return null;

            var controllerRequestRoute = Url.RemoveQueryParameters(GetControllerRequestRoute(requestRoute, routeConfiguration.Route));

            var routes = routeConfiguration.MethodRoutes;
            if (routes.IsNullOrEmpty()) return null;

            foreach (var methodRoute in routes)
            {
                if (endpointType == TrakHoundApiEndpointType.Stream)
                {
                    if (methodRoute.RequestType != TrakHoundApiEndpointType.Stream) continue;
                }
                else if (methodRoute.RequestMethod != method)
                {
                    continue;
                }

                // The Endpoint Information (with its Api and Controller) is set when the Route is loaded
                if (IsRouteMatch(controllerRequestRoute, methodRoute)) return methodRoute.EndpointInformation;
            }

            return null;
        }

        public async Task<bool> ReloadConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                var configurations = _configurationProfile.Get<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory);
                if (!configurations.IsNullOrEmpty())
                {
                    var configuration = configurations.FirstOrDefault(o => o.Id == configurationId);
                    if (configuration != null)
                    {
                        if (!string.IsNullOrEmpty(configuration.Route))
                        {
                            var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                            if (module != null)
                            {
                                var route = RewriteRoute(configuration.Route);
                                if (!route.StartsWith('/')) route = '/' + route;

                                var loadHash = GetLoadHash(configuration);

                                await _loadLock.WaitAsync();
                                try
                                {
                                    await LoadConfiguration(configuration, route, module);
                                    SetInstalledHashes(configuration, module, loadHash);
                                }
                                finally
                                {
                                    _loadLock.Release();
                                }

                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        public async Task<TrakHoundApiResponse> HandleRequest(
            TrakHoundApiMethod requestMethod,
            string requestRoute,
            Stream requestBody,
            string requestContentType = "application/octet-stream",
            Dictionary<string, string> queryParameters = null,
            TrakHoundApiRequestSource source = null,
            IDictionary<string, string> requestHeaders = null,
            CancellationToken cancellationToken = default
            )
        {
            var response = new TrakHoundApiResponse();

            if (!string.IsNullOrEmpty(requestRoute))
            {
                requestRoute = RewriteRoute(requestRoute);
                if (!requestRoute.StartsWith('/')) requestRoute = '/' + requestRoute;

                var debug = _logger.IsEnabled(TrakHoundLogLevel.Debug); // Skips formatting the messages when Debug isn't received
                if (debug) _logger.LogDebug($"API Request Received : {requestRoute}");
                var stpw = System.Diagnostics.Stopwatch.StartNew();

                if (TryGetRouteConfiguration(requestRoute, out var routeConfiguration) && !routeConfiguration.Controllers.IsNullOrEmpty())
                {
                    // Remove Api Configuration Route prefix
                    var route = GetControllerRequestRoute(requestRoute, routeConfiguration.Route);
                    var parameters = GetRequestParameters(route, queryParameters);

                    // Remove Query Parameters
                    route = Url.RemoveQueryParameters(route);

                    // Check the call against the Zones of the caller and the Api
                    var zoneDecision = AuthorizeZone(source, routeConfiguration.Configuration, route, TrakHoundZoneResources.GetApiPermission(requestMethod));
                    if (zoneDecision.IsAllowed)
                    {
                        response = await ProcessRequest(routeConfiguration.MethodRoutes, requestMethod, route, Url.RemoveQueryParameters(requestRoute), parameters, TrakHoundApiHeaders.Filter(requestHeaders), requestBody, requestContentType, source, cancellationToken);
                    }
                    else
                    {
                        response = new TrakHoundApiResponse(403, $"Zone Access Denied : {zoneDecision.Message}".ToUtf8Bytes(), "text/plain");
                    }
                }

                stpw.Stop();
                if (debug) _logger.LogDebug($"API Response Received : {stpw.ElapsedMilliseconds}ms : Status Code = {response.StatusCode} : {requestRoute}");
            }

            return response;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> HandleStream(
            string requestRoute,
            Stream requestBody,
            string requestContentType = "application/octet-stream",
            Dictionary<string, string> queryParameters = null,
            TrakHoundApiRequestSource source = null,
            IDictionary<string, string> requestHeaders = null,
            CancellationToken cancellationToken = default
            )
        {
            if (!string.IsNullOrEmpty(requestRoute))
            {
                requestRoute = RewriteRoute(requestRoute);
                if (!requestRoute.StartsWith('/')) requestRoute = '/' + requestRoute;

                var debug = _logger.IsEnabled(TrakHoundLogLevel.Debug);
                if (debug) _logger.LogDebug($"API Stream Request Received : {requestRoute}");
                var stpw = System.Diagnostics.Stopwatch.StartNew();

                ITrakHoundStream<TrakHoundApiResponse> stream = null;

                if (TryGetRouteConfiguration(requestRoute, out var routeConfiguration) && !routeConfiguration.Controllers.IsNullOrEmpty())
                {
                    var configuration = routeConfiguration.Configuration;

                    // Remove Api Configuration Route prefix
                    var route = GetControllerRequestRoute(requestRoute, routeConfiguration.Route);
                    var parameters = GetRequestParameters(route, queryParameters);

                    // Remove Query Parameters
                    route = Url.RemoveQueryParameters(route);

                    // Check the call against the Zones of the caller and the Api (a Stream only reads)
                    var zoneDecision = AuthorizeZone(source, configuration, route, TrakHoundZoneResources.ReadPermission);
                    if (!zoneDecision.IsAllowed) return null;

                    stream = await ProcessStream(routeConfiguration.MethodRoutes, route, Url.RemoveQueryParameters(requestRoute), parameters, TrakHoundApiHeaders.Filter(requestHeaders), requestBody, source, cancellationToken);
                    if (stream != null)
                    {
                        var consumerKey = configuration.Id;
                        var consumer = stream;
                        _consumers.Add(consumerKey, consumer);
                        consumer.Disposed += (s, id) => _consumers.Remove(consumerKey, consumer);
                    }
                }

                stpw.Stop();
                if (debug) _logger.LogDebug($"API Stream Response Received : {stpw.ElapsedMilliseconds}ms : {requestRoute}");

                return stream;
            }

            return null;
        }

        private TrakHoundZoneDecision AuthorizeZone(TrakHoundApiRequestSource source, ITrakHoundApiConfiguration configuration, string route, string permission)
        {
            // A Request that changes something, made by the Admin Interface itself (not a Module), needs the signed in Admin to be able to change
            // Modules (this Api, for an Admin whose Role is limited to Zones)
            if (permission != TrakHoundZoneResources.ReadPermission && TrakHoundZoneResources.GetMemberId(source) == null)
            {
                var adminTarget = TrakHound.Instance.Security.TrakHoundAdminTarget.Module(TrakHoundZoneResources.ApiPrefix, configuration?.Id);
                var denial = TrakHound.Instance.Security.TrakHoundAdminContext.GetDenial(TrakHound.Instance.Security.TrakHoundAdminSecurity.Modules, TrakHound.Instance.Security.TrakHoundAdminSecurity.WritePermission, adminTarget);
                if (denial != null) return TrakHoundZoneDecision.Denied(null, denial);
            }

            var zoneManager = _instance?.ZoneManager;
            if (zoneManager == null || configuration == null) return TrakHoundZoneDecision.Allowed;

            var targetMemberId = TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.ApiPrefix, configuration.Id);
            var resourceId = TrakHoundZoneResources.GetApiResourceId(configuration.Id, route);
            return zoneManager.Authorize(source, targetMemberId, resourceId, permission);
        }

        private static string GetControllerRequestRoute(string requestRoute, string apiRoute)
        {
            // The API Route prefix has already been matched (see TryGetRouteConfiguration)
            var basePath = apiRoute.TrimEnd('/');
            var remainder = requestRoute.Substring(basePath.Length);

            var queryIndex = remainder.IndexOf('?');
            var path = queryIndex >= 0 ? remainder.Substring(0, queryIndex) : remainder;
            var query = queryIndex >= 0 ? remainder.Substring(queryIndex) : string.Empty;

            return '/' + path.Trim('/') + query;
        }

        private static Dictionary<string, string> GetRequestParameters(string route, Dictionary<string, string> queryParameters)
        {
            var parameters = new Dictionary<string, string>();

            // Parameters passed as method arguments take priority over Parameters passed in the URL query string
            AddRequestParameters(parameters, queryParameters);
            AddRequestParameters(parameters, Url.GetQueryParameters(route));

            return parameters;
        }

        private static void AddRequestParameters(Dictionary<string, string> parameters, Dictionary<string, string> inputParameters)
        {
            if (!inputParameters.IsNullOrEmpty())
            {
                foreach (var inputParameter in inputParameters)
                {
                    if (!string.IsNullOrEmpty(inputParameter.Key))
                    {
                        parameters.TryAdd(inputParameter.Key.ToLower(), inputParameter.Value);
                    }
                }
            }
        }


        #region "Information"

        public IEnumerable<TrakHoundApiMetrics> GetMetrics() => _connectionManager.GetMetrics();

        public TrakHoundApiMetrics GetMetrics(string apiId) => _connectionManager.GetMetrics(apiId);

        public TrakHoundApiRecentRequests GetRecentRequests() => _connectionManager.GetRecentRequests();

        public TrakHoundApiStreams GetStreams() => _streamManager?.Get() ?? new TrakHoundApiStreams();

        public bool CloseStream(string streamId) => _streamManager?.Close(streamId) ?? false;

        public IEnumerable<TrakHoundApiInformation> GetRouteInformation()
        {
            var informations = new List<TrakHoundApiInformation>();

            var routes = _routes;
            if (!routes.IsNullOrEmpty())
            {
                foreach (var route in routes)
                {
                    if (route.Information != null)
                    {
                        informations.Add(route.Information);
                    }
                }
            }

            return informations;
        }

        public IEnumerable<TrakHoundApiInformation> GetPackageInformation()
        {
            return new List<TrakHoundApiInformation>();
        }

        public TrakHoundApiInformation GetInformation(string apiId)
        {
            if (!string.IsNullOrEmpty(apiId))
            {
                var routes = _routes;
                if (!routes.IsNullOrEmpty())
                {
                    foreach (var route in routes)
                    {
                        if (route.Configuration?.Id == apiId && !route.Controllers.IsNullOrEmpty())
                        {
                            return route.Information;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The OpenAPI Document (JSON) describing the API's Endpoints (null if the API is not loaded)
        /// </summary>
        public string GetOpenApiDocument(string apiId)
        {
            if (!string.IsNullOrEmpty(apiId))
            {
                ApiRouteConfiguration? apiRoute = null;

                lock (_lock)
                {
                    if (!_routes.IsNullOrEmpty())
                    {
                        foreach (var route in _routes)
                        {
                            if (route.Configuration?.Id == apiId && !route.Controllers.IsNullOrEmpty())
                            {
                                apiRoute = route;
                                break;
                            }
                        }
                    }
                }

                // Built outside the lock (reflection and Schema generation), and cached until the Configuration or Package changes
                if (apiRoute.HasValue)
                {
                    var route = apiRoute.Value;
                    if (_openApiDocuments.TryGetValue(apiId, out var cached) && cached.Key == route.Key) return cached.Document;

                    // Kept per Api (replaced when the Route Key changes) so old Documents don't accumulate
                    var document = TrakHoundApiOpenApi.CreateJson(route.Configuration, route.Package, route.Controllers.Select(o => o.GetType()));
                    _openApiDocuments[apiId] = (route.Key, document);
                    return document;
                }
            }

            return null;
        }

        /// <summary>
        /// The Apis that serve MCP Tools (their mcpTools is Marked or All). Each has a Key that changes when its
        /// Configuration or Package changes, and creates its OpenAPI Document (with the "x-trakhound-mcp" extension) when asked
        /// </summary>
        public IEnumerable<TrakHoundApiMcpSource> GetMcpSources()
        {
            List<ApiRouteConfiguration> routes;
            lock (_lock)
            {
                routes = _routes?.Where(o => o.Configuration != null && o.Configuration.McpTools != TrakHoundApiMcpToolsMode.Off && !o.Controllers.IsNullOrEmpty()).ToList();
            }

            if (routes.IsNullOrEmpty()) return Enumerable.Empty<TrakHoundApiMcpSource>();

            return routes.Select(route =>
            {
                var configuration = route.Configuration;
                var package = route.Package;
                var controllerTypes = route.Controllers.Select(o => o.GetType()).ToList();
                var key = route.Key;

                // Built when asked (reflection and Schema generation), outside the lock
                Func<System.Text.Json.Nodes.JsonObject> createDocument = () => TrakHoundApiOpenApi.Create(configuration, package, controllerTypes, new TrakHoundApiOpenApiOptions
                {
                    McpTools = configuration.McpTools,
                    McpInclude = configuration.McpInclude?.ToList(),
                    McpExclude = configuration.McpExclude?.ToList(),
                    McpReadOnly = configuration.McpReadOnly
                });

                return new TrakHoundApiMcpSource(configuration.Id, GetCatalogRoute(configuration.Route), key, createDocument);
            }).ToList();
        }

        /// <summary>
        /// The Apis served over HTTP with the Endpoints that the Identity (null for anonymous) is allowed to call. Apis without any are not included
        /// </summary>
        public IEnumerable<TrakHoundApiCatalogApi> GetCatalog(ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            var apis = new List<TrakHoundApiCatalogApi>();

            foreach (var route in GetHttpRoutes())
            {
                var api = CreateCatalogApi(route, authorizer, identity);
                if (api != null) apis.Add(api);
            }

            return apis.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// An Api served over HTTP with the Endpoints that the Identity (null for anonymous) is allowed to call. Null if it isn't served over HTTP or has none
        /// </summary>
        public TrakHoundApiCatalogApi GetCatalogApi(string apiId, ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            if (string.IsNullOrEmpty(apiId)) return null;

            // Only the one Api is checked (instead of building the whole Catalog to find it)
            var route = GetHttpRoutes().FirstOrDefault(o => o.Configuration.Id == apiId);
            return route.Configuration != null ? CreateCatalogApi(route, authorizer, identity) : null;
        }

        private static TrakHoundApiCatalogApi CreateCatalogApi(ApiRouteConfiguration route, ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            var information = route.Information;
            if (information == null) return null;

            var endpoints = new List<TrakHoundApiCatalogEndpoint>();
            foreach (var controllerInformation in information.Controllers ?? Enumerable.Empty<TrakHoundApiControllerInformation>())
            {
                if (controllerInformation == null) continue;

                foreach (var endpointInformation in controllerInformation.EndPoints ?? Enumerable.Empty<TrakHoundApiEndpointInformation>())
                {
                    var resourceId = TrakHoundApiInformation.GetEndpointResourceId(information.Id, controllerInformation.Id, endpointInformation.Id);
                    var access = GetCatalogAccess(authorizer, identity, resourceId);
                    if (!access.Allowed) continue;

                    var endpoint = new TrakHoundApiCatalogEndpoint();
                    endpoint.Id = endpointInformation.Id;
                    endpoint.ControllerId = controllerInformation.Id;
                    endpoint.Controller = controllerInformation.Name;
                    endpoint.ControllerDescription = controllerInformation.Description;
                    endpoint.Group = endpointInformation.Group;
                    endpoint.Type = endpointInformation.Type;
                    endpoint.Method = endpointInformation.Method;
                    endpoint.Route = GetCatalogRoute(information.Route, controllerInformation.Route, endpointInformation.Route);
                    endpoint.Description = endpointInformation.Description;
                    endpoint.Parameters = endpointInformation.Parameters;
                    endpoint.Responses = endpointInformation.Responses;
                    endpoint.AuthenticationRequired = access.AuthenticationRequired;
                    endpoints.Add(endpoint);
                }
            }

            if (endpoints.Count < 1) return null;

            var api = new TrakHoundApiCatalogApi();
            api.Id = information.Id;
            api.Name = !string.IsNullOrEmpty(route.Configuration.Name) ? route.Configuration.Name : route.Configuration.PackageId ?? route.Configuration.Id;
            api.Description = route.Configuration.Description;
            api.Route = information.Route;
            api.Version = information.PackageVersion;
            api.Endpoints = endpoints;
            return api;
        }

        /// <summary>
        /// The OpenAPI Document (JSON) for an Api served over HTTP, describing only the Endpoints that the Identity (null for anonymous) is allowed
        /// to call. Null if the Api isn't served over HTTP or the Identity can't call any of its Endpoints
        /// </summary>
        public string GetCatalogOpenApiDocument(string apiId, ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity, string serverUrl = null, string apiKeyHeader = null)
        {
            if (string.IsNullOrEmpty(apiId)) return null;

            var route = GetHttpRoutes().FirstOrDefault(o => o.Configuration.Id == apiId);
            if (route.Configuration == null) return null;

            var configurationId = route.Configuration.Id;
            Func<Type, TrakHoundApiEndpointRouteAttribute, (bool Allowed, bool AuthenticationRequired)> getAccess = (controllerType, routeAttribute) =>
            {
                var resourceId = TrakHoundApiInformation.GetEndpointResourceId(configurationId, TrakHoundApiInformation.GetControllerId(configurationId, controllerType), TrakHoundApiInformation.GetEndpointId(routeAttribute));
                return GetCatalogAccess(authorizer, identity, resourceId);
            };

            var controllerTypes = route.Controllers.Select(o => o.GetType()).ToList();

            // The Document only depends on which Endpoints the caller can use (and whether they need Authentication), so it is
            // built once for each combination (from the Information built when the Route was loaded) instead of for every request
            string cacheKey = null;
            var access = GetCatalogAccessSignature(route, authorizer, identity);
            if (access != null)
            {
                // Not found when the Identity can't call any of the Endpoints (the same as an Api that doesn't exist)
                if (access.IndexOf('a') < 0 && access.IndexOf('r') < 0) return null;

                cacheKey = $"{apiId}|{route.Key}|{access}|{serverUrl}|{apiKeyHeader}";
                if (_catalogDocuments.TryGetValue(cacheKey, out var cachedDocument)) return cachedDocument;
            }
            else
            {
                // Not found when the Identity can't call any of the Endpoints (the same as an Api that doesn't exist)
                var anyAllowed = controllerTypes.Any(controllerType => controllerType.GetMethods()
                    .Where(TrakHoundApi.IsEndpointMethod)
                    .SelectMany(o => o.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>())
                    .Any(o => getAccess(controllerType, o).Allowed));
                if (!anyAllowed) return null;
            }

            var options = new TrakHoundApiOpenApiOptions();
            options.EndpointFilter = (controllerType, routeAttribute) => getAccess(controllerType, routeAttribute).Allowed;
            options.EndpointSecurity = (controllerType, routeAttribute) => getAccess(controllerType, routeAttribute).AuthenticationRequired;
            options.IncludeSecuritySchemes = true;
            if (!string.IsNullOrEmpty(apiKeyHeader)) options.ApiKeyHeader = apiKeyHeader;
            options.ServerUrl = serverUrl;
            options.IncludePackageDetails = false;

            var document = TrakHoundApiOpenApi.CreateJson(route.Configuration, route.Package, controllerTypes, options);

            if (cacheKey != null && document != null)
            {
                // The Server URL comes from the request, so the cache is limited (cleared when full)
                if (_catalogDocuments.Count >= _maxCatalogDocuments) _catalogDocuments.Clear();
                _catalogDocuments[cacheKey] = document;
            }

            return document;
        }

        // One character per Endpoint : 'n' not allowed, 'a' allowed, 'r' allowed with Authentication required. Null without Information
        private static string GetCatalogAccessSignature(ApiRouteConfiguration route, ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            var information = route.Information;
            if (information?.Controllers == null) return null;

            var signature = new StringBuilder();
            foreach (var controllerInformation in information.Controllers)
            {
                if (controllerInformation?.EndPoints == null) continue;

                foreach (var endpointInformation in controllerInformation.EndPoints)
                {
                    var resourceId = TrakHoundApiInformation.GetEndpointResourceId(information.Id, controllerInformation.Id, endpointInformation.Id);
                    var access = GetCatalogAccess(authorizer, identity, resourceId);
                    signature.Append(!access.Allowed ? 'n' : access.AuthenticationRequired ? 'r' : 'a');
                }
            }

            return signature.ToString();
        }

        // Apis that are loaded and served over HTTP (copied so they can be read outside of the lock)
        private List<ApiRouteConfiguration> GetHttpRoutes()
        {
            lock (_lock)
            {
                return _routes?.Where(o => o.Configuration != null && o.Configuration.HttpEnabled && !o.Controllers.IsNullOrEmpty()).ToList() ?? new List<ApiRouteConfiguration>();
            }
        }

        // An Endpoint is listed when the Identity can call it. Authentication is required when an anonymous caller can't
        private static (bool Allowed, bool AuthenticationRequired) GetCatalogAccess(ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity, string resourceId)
        {
            if (authorizer == null) return (true, false);

            var allowed = authorizer.Authorize(identity, resourceId).IsAllowed;
            if (!allowed) return (false, false);

            var anonymousAllowed = identity == null || authorizer.Authorize(null, resourceId).IsAllowed;
            return (true, !anonymousAllowed);
        }

        private static string GetCatalogRoute(params string[] routes)
        {
            var route = Url.Combine(routes) ?? "";
            return "/" + route.TrimStart('/');
        }

        private static TrakHoundApiInformation CreateInformation(ApiRouteConfiguration route)
        {
            return TrakHoundApiInformation.Create(route.Controllers, route.Configuration, route.Package, route.VolumeId, route.PackageReadMe);
        }

        #endregion

        #region "Process"

        internal class MethodRoute
        {
            public TrakHoundApiController Controller { get; set; }

            public TrakHoundApiEndpointType RequestType { get; set; }

            public TrakHoundApiMethod? RequestMethod { get; set; }

            public string ControllerRoute { get; set; }

            public string EndpointRoute { get; set; }

            public string Route { get; set; }

            public MethodInfo Method { get; set; }

            // Set when the Route is loaded, so they aren't created for every Request
            public Regex RouteRegex { get; set; }

            public int RouteSegments { get; set; }

            public bool IsWildcard { get; set; }

            public TrakHoundApiEndpointInformation EndpointInformation { get; set; }

            public ITrakHoundLogger Logger { get; set; }

            // Set on the first Request, so the Monitor isn't looked up for every Request
            public TrakHoundCpuMonitor CpuMonitor { get; set; }

            public override string ToString()
            {
                return $"{Route} => {Method?.Name}";
            }
        }

        class MethodParameters
        {
            public Dictionary<string, MethodRouteParameter> RouteParameters { get; set; } = new();
            public Dictionary<string, MethodQueryParameter> QueryParameters { get; set; } = new();
            public List<MethodHeaderParameter> HeaderParameters { get; set; } = new();
            public MethodBodyParameter BodyParameter { get; set; }
            public int[] CancellationTokenIndexes { get; set; } = Array.Empty<int>(); // Parameters set to the Request's CancellationToken
            public object[] DefaultValues { get; set; } = Array.Empty<object>(); // Initial Invoke arguments (Default Value or null for each Method Parameter)
        }

        class MethodRouteParameter
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public ParameterInfo Parameter { get; set; }
            public FromRouteAttribute Attribute { get; set; }
        }

        class MethodQueryParameter
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public ParameterInfo Parameter { get; set; }
            public FromQueryAttribute Attribute { get; set; }
        }

        class MethodHeaderParameter
        {
            public string Name { get; set; } // The Header Name
            public int Index { get; set; }
            public ParameterInfo Parameter { get; set; }
        }

        class MethodBodyParameter
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public ParameterInfo Parameter { get; set; }
            public FromBodyAttribute Attribute { get; set; }
        }


        private MethodParameters GetMethodParameters(MethodInfo methodInfo)
        {
            return _cachedParameters.GetOrAdd(methodInfo, CreateMethodParameters);
        }

        private static MethodParameters CreateMethodParameters(MethodInfo methodInfo)
        {
            var methodParameters = new MethodParameters();

            var parameters = methodInfo.GetParameters();
            if (!parameters.IsNullOrEmpty())
            {
                methodParameters.DefaultValues = parameters.Select(o => o.HasDefaultValue ? o.DefaultValue : null).ToArray();
                methodParameters.CancellationTokenIndexes = parameters.Where(o => o.ParameterType == typeof(CancellationToken)).Select(o => o.Position).ToArray();

                for (var i = 0; i < parameters.Length; i++)
                {
                    var methodParameter = parameters[i];
                    string methodParameterName = methodParameter.Name.ToLower();

                    // Route Parameters
                    var routeAttribute = methodParameter.GetCustomAttribute<FromRouteAttribute>();
                    if (routeAttribute != null)
                    {
                        var routeParameter = new MethodRouteParameter();
                        routeParameter.Name = methodParameterName;
                        routeParameter.Index = i;
                        routeParameter.Parameter = methodParameter;
                        routeParameter.Attribute = routeAttribute;
                        methodParameters.RouteParameters.Add(routeParameter.Name, routeParameter);
                    }

                    // Query Parameters
                    var queryAttribute = methodParameter.GetCustomAttribute<FromQueryAttribute>();
                    if (queryAttribute != null)
                    {
                        var queryParameter = new MethodQueryParameter();
                        queryParameter.Name = !string.IsNullOrEmpty(queryAttribute.ParameterName) ? queryAttribute.ParameterName.ToLower() : methodParameterName;
                        queryParameter.Index = i;
                        queryParameter.Parameter = methodParameter;
                        queryParameter.Attribute = queryAttribute;
                        methodParameters.QueryParameters.Add(queryParameter.Name, queryParameter);
                    }

                    // Header Parameters
                    var headerAttribute = methodParameter.GetCustomAttribute<FromHeaderAttribute>();
                    if (headerAttribute != null)
                    {
                        var headerParameter = new MethodHeaderParameter();
                        headerParameter.Name = FromHeaderAttribute.GetHeaderName(headerAttribute, methodParameter.Name);
                        headerParameter.Index = i;
                        headerParameter.Parameter = methodParameter;
                        methodParameters.HeaderParameters.Add(headerParameter);
                    }

                    // Body Parameters
                    var bodyAttribute = methodParameter.GetCustomAttribute<FromBodyAttribute>();
                    if (bodyAttribute != null)
                    {
                        var bodyParameter = new MethodBodyParameter();
                        bodyParameter.Name = methodParameterName;
                        bodyParameter.Index = i;
                        bodyParameter.Parameter = methodParameter;
                        bodyParameter.Attribute = bodyAttribute;
                        methodParameters.BodyParameter = bodyParameter;
                    }
                }
            }

            return methodParameters;
        }


        private IEnumerable<MethodRoute> GetRoutes(IEnumerable<TrakHoundApiController> controllers)
        {
            if (!controllers.IsNullOrEmpty())
            {
                var routes = new List<MethodRoute>();

                foreach (var controller in controllers)
                {
                    // Cache per Controller instance (not per Type) so that multiple configurations using the same
                    // package, and reloaded configurations, each dispatch to their own Controller instance
                    routes.AddRange(_controllerRoutes.GetValue(controller, CreateControllerRoutes));
                }

                return routes;
            }

            return null;
        }

        private static MethodRoute[] CreateControllerRoutes(TrakHoundApiController controller)
        {
            var controllerType = controller.GetType();
            var controllerRoute = Url.PathSeparator.ToString();
            var controllerRoutes = new List<MethodRoute>();

            var controllerAttribute = controllerType.GetCustomAttribute<TrakHoundApiControllerAttribute>();
            if (controllerAttribute != null)
            {
                controllerRoute = controllerAttribute.Route;
            }

            var methods = controllerType.GetMethods();
            if (!methods.IsNullOrEmpty())
            {
                foreach (var method in methods)
                {
                    if (TrakHoundApi.IsEndpointMethod(method))
                    {
                        var endPointAttributes = method.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>();
                        if (!endPointAttributes.IsNullOrEmpty())
                        {
                            foreach (var endPointAttribute in endPointAttributes)
                            {
                                var route = new MethodRoute();
                                route.RequestType = endPointAttribute.Type;
                                route.RequestMethod = endPointAttribute.Method;
                                route.Controller = controller;
                                route.ControllerRoute = controllerRoute;
                                route.EndpointRoute = endPointAttribute.Route;
                                route.Route = Url.Combine(controllerRoute, endPointAttribute.Route);
                                if (string.IsNullOrEmpty(route.Route)) route.Route = "/";
                                if (!route.Route.StartsWith('/')) route.Route = '/' + route.Route;
                                route.RouteRegex = new Regex(CreateRouteRegexPattern(route.Route), RegexOptions.Compiled);
                                route.RouteSegments = CountSegments(route.Route);
                                route.IsWildcard = route.Route.Contains('*');
                                route.Method = method;
                                controllerRoutes.Add(route);
                            }
                        }
                    }
                }
            }

            return controllerRoutes.OrderByDescending(o => o.Route).ToArray();
        }


        private async Task<TrakHoundApiResponse> ProcessRequest(
            MethodRoute[] routes,
            TrakHoundApiMethod requestMethod,
            string requestRoute,
            string requestPath,
            Dictionary<string, string> queryParameters,
            Dictionary<string, string> requestHeaders,
            Stream requestBody,
            string requestContentType = "application/octet-stream",
            TrakHoundApiRequestSource source = null,
            CancellationToken cancellationToken = default
            )
        {
            if (!routes.IsNullOrEmpty())
            {
                var taskId = Task.CurrentId;
                ResourceManager.AddTask(taskId, requestRoute);

                try
                {
                    var response = new TrakHoundApiResponse();
                    response.StatusCode = 404;

                    // Routes are already sorted in the order they are matched
                    foreach (var route in routes)
                    {
                        if (IsRouteMatch(requestRoute, route))
                        {
                            if (route.RequestMethod != requestMethod)
                            {
                                // Route matches but Method does not (keep checking for a Route with a matching Method)
                                response.StatusCode = 405;
                            }
                            else
                            {
                                var requestId = Guid.NewGuid().ToString();
                                var requestedRoute = !string.IsNullOrEmpty(route.Route) ? route.Route : "/";

                                var logger = GetEndpointLogger(route);
                                var debug = logger.IsEnabled(TrakHoundLogLevel.Debug); // Skips formatting the messages when Debug isn't received
                                if (debug) logger.LogDebug($"Request Received : {route.RequestType} : {requestId} : Method = {requestMethod} : Route = '{requestedRoute}'");
                                var requestBodyLength = LogRequestParameters(logger, route, requestId, queryParameters, requestBody);

                                var requestTimestamp = DateTime.UtcNow;
                                var stpw = System.Diagnostics.Stopwatch.StartNew();

                                try
                                {
                                    // Measure the CPU used by the Endpoint (and anything it starts)
                                    var cpuMonitor = GetCpuMonitor(route, requestMethod, requestedRoute);
                                    response = cpuMonitor != null
                                        ? await cpuMonitor.Run(() => ProcessRoute(route.Controller, route.Method, route.Route, requestRoute, queryParameters, requestHeaders, requestBody, cancellationToken))
                                        : await ProcessRoute(route.Controller, route.Method, route.Route, requestRoute, queryParameters, requestHeaders, requestBody, cancellationToken);
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    if (debug) logger.LogDebug($"Request Response : {route.RequestType} : {requestId} : Cancelled");
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    var exception = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                                    logger.LogError($"Request Response : {route.RequestType} : {requestId} : Exception : {exception.HResult} : {exception.Message} : {TrakHoundLogger.GetExceptionStackTrace(exception)}");

                                    response = TrakHoundApiResponse.InternalError(exception.Message);
                                }

                                long responseBodyLength = 0;
                                if (response.Content != null && response.Content.CanSeek) responseBodyLength = response.Content.Length;
                                string errorText = null;

                                stpw.Stop();
                                var responseMilliseconds = stpw.Elapsed.TotalMilliseconds;
                                var responseTimestamp = DateTime.UtcNow;

                                if (response.Success)
                                {
                                    if (debug) logger.LogDebug($"Request Response : {route.RequestType} : {requestId} : Status Code = {response.StatusCode} : Duration = {Math.Round(responseMilliseconds, 4)}ms");
                                }
                                else if (debug || response.StatusCode >= 400)
                                {
                                    // Reading the Content disposes its Stream, so it is replaced with a copy that can still be sent
                                    // (otherwise an error Response with a message, ex. BadRequest("..."), failed to write and returned 500)
                                    var contentBytes = response.GetContentBytes();
                                    response.Content = contentBytes != null ? new MemoryStream(contentBytes) : null;
                                    var contentText = contentBytes != null ? Encoding.UTF8.GetString(contentBytes) : null;
                                    if (response.StatusCode >= 400) errorText = contentText; // Kept in the Recent Requests

                                    if (response.StatusCode >= 500 && response.StatusCode < 600)
                                    {
                                        logger.LogError($"Request Response : {route.RequestType} : {requestId} : Status Code = {response.StatusCode} : Duration = {Math.Round(responseMilliseconds, 4)}ms : {contentText}");
                                    }
                                    else
                                    {
                                        logger.LogDebug($"Request Response : {route.RequestType} : {requestId} : Status Code = {response.StatusCode} : Duration = {Math.Round(responseMilliseconds, 4)}ms : {contentText}");
                                    }
                                }

                                var request = CreateRequestRecord(route, requestId, requestMethod, requestedRoute, requestPath, queryParameters, requestBodyLength, requestTimestamp, source);
                                request.StatusCode = response.StatusCode;
                                request.ResponseBytes = responseBodyLength;
                                request.Error = TruncateError(errorText);
                                request.Completed = responseTimestamp;
                                _connectionManager.Update(request);

                                break;
                            }
                        }
                    }

                    return response;
                }
                finally
                {
                    ResourceManager.RemoveTask(taskId);
                }
            }

            return new TrakHoundApiResponse();
        }

        private async Task<ITrakHoundStream<TrakHoundApiResponse>> ProcessStream(
            MethodRoute[] routes,
            string requestRoute,
            string requestPath,
            Dictionary<string, string> queryParameters,
            Dictionary<string, string> requestHeaders,
            Stream requestBody,
            TrakHoundApiRequestSource source = null,
            CancellationToken cancellationToken = default
            )
        {
            if (!routes.IsNullOrEmpty())
            {
                // Routes are already sorted in the order they are matched
                foreach (var route in routes)
                {
                    if (route.RequestType == TrakHoundApiEndpointType.Stream)
                    {
                        if (IsRouteMatch(requestRoute, route))
                        {
                            var requestId = Guid.NewGuid().ToString();
                            var requestedRoute = !string.IsNullOrEmpty(route.Route) ? route.Route : "/";

                            var logger = GetEndpointLogger(route);
                            var debug = logger.IsEnabled(TrakHoundLogLevel.Debug);
                            if (debug) logger.LogDebug($"Request Received : {route.RequestType} : {requestId} : Route = '{requestedRoute}'");
                            var requestBodyLength = LogRequestParameters(logger, route, requestId, queryParameters, requestBody);

                            var requestTimestamp = DateTime.UtcNow;
                            var stpw = System.Diagnostics.Stopwatch.StartNew();

                            // Measure the CPU used by the Stream Endpoint (including the Messages its code pushes after the Stream opens)
                            var cpuMonitor = GetCpuMonitor(route, TrakHoundApiMethod.GET, requestedRoute);
                            var stream = cpuMonitor != null
                                ? await cpuMonitor.Run(() => ProcessStreamRoute(route.Controller, route.Method, route.Route, requestRoute, queryParameters, requestHeaders, requestBody, logger, requestId, cancellationToken))
                                : await ProcessStreamRoute(route.Controller, route.Method, route.Route, requestRoute, queryParameters, requestHeaders, requestBody, logger, requestId, cancellationToken);

                            stpw.Stop();
                            var responseMilliseconds = stpw.Elapsed.TotalMilliseconds;
                            var responseTimestamp = DateTime.UtcNow;
                            if (debug) logger.LogDebug($"Request Response : {route.RequestType} : {requestId} : Duration = {Math.Round(responseMilliseconds, 4)}ms");

                            var request = CreateRequestRecord(route, requestId, TrakHoundApiMethod.GET, requestedRoute, requestPath, queryParameters, requestBodyLength, requestTimestamp, source);
                            request.Completed = responseTimestamp;
                            _connectionManager.Update(request);

                            if (stream != null) _streamManager.Add(stream, CreateStreamInformation(request));

                            return stream;
                        }
                    }
                }
            }

            return null;
        }

        private const int MaxErrorLength = 1000;

        /// <summary>
        /// The Recent Requests entry for a Request (values of sensitive Query Parameters are masked, as they are for HTTP Requests)
        /// </summary>
        private static TrakHoundApiRequestRecord CreateRequestRecord(
            MethodRoute route,
            string requestId,
            TrakHoundApiMethod method,
            string requestedRoute,
            string requestPath,
            Dictionary<string, string> queryParameters,
            long requestBodyLength,
            DateTime requested,
            TrakHoundApiRequestSource source
            )
        {
            source ??= TrakHoundApiRequestSource.Instance;

            var request = new TrakHoundApiRequestRecord();
            request.RequestId = requestId;
            request.ApiId = route.Controller.Id;
            request.PackageId = route.Controller.Package?.Id;
            request.PackageVersion = route.Controller.Package?.Version;
            request.ControllerId = route.EndpointInformation?.Controller?.Id;
            request.EndpointId = route.EndpointInformation?.Id;
            request.EndpointType = route.RequestType;
            request.Method = method;
            request.Route = requestedRoute;
            request.Path = requestPath;
            request.SourceType = source.Type;
            request.SourceId = source.Id;
            request.SourceName = source.Name;
            request.RequestBytes = requestBodyLength;
            request.Requested = requested;

            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    // The Client Address is already the Source's Name
                    if (queryParameter.Key == TrakHoundApi.ClientAddressParameter) continue;
                    if (queryParameter.Key == TrakHoundApi.ClientAgentParameter)
                    {
                        request.ClientAgent = queryParameter.Value;
                        continue;
                    }

                    request.Query ??= new Dictionary<string, string>();
                    request.Query[queryParameter.Key] = TrakHound.Http.TrakHoundHttpQueryMask.IsSensitive(queryParameter.Key) ? TrakHound.Http.TrakHoundHttpQueryMask.Mask : queryParameter.Value;
                }
            }

            return request;
        }

        /// <summary>
        /// The API Streams entry for a Stream (from the Recent Requests entry of the Request that opened it)
        /// </summary>
        private static TrakHoundApiStreamInformation CreateStreamInformation(TrakHoundApiRequestRecord request)
        {
            return new TrakHoundApiStreamInformation
            {
                RequestId = request.RequestId,
                ApiId = request.ApiId,
                PackageId = request.PackageId,
                PackageVersion = request.PackageVersion,
                ControllerId = request.ControllerId,
                EndpointId = request.EndpointId,
                Route = request.Route,
                Path = request.Path,
                Query = request.Query,
                SourceType = request.SourceType,
                SourceId = request.SourceId,
                SourceName = request.SourceName,
                ClientAgent = request.ClientAgent,
                Opened = request.Completed
            };
        }

        private static string TruncateError(string error)
        {
            if (string.IsNullOrEmpty(error)) return null;
            return error.Length > MaxErrorLength ? error.Substring(0, MaxErrorLength) + "..." : error;
        }

        private static TrakHoundCpuMonitor GetCpuMonitor(MethodRoute route, TrakHoundApiMethod method, string requestedRoute)
        {
            if (route.CpuMonitor != null) return route.CpuMonitor;

            var monitors = TrakHoundModuleCpuMonitors.Default;
            if (!monitors.Enabled || string.IsNullOrEmpty(route.Controller?.Id)) return null;

            var type = TrakHound.Instances.TrakHoundCpuModuleTypes.Api;
            monitors.GetMonitor(type, route.Controller.Id, route.Controller.Package?.Id);
            route.CpuMonitor = monitors.GetChildMonitor(type, route.Controller.Id, TrakHoundApiConnectionManager.GetCpuKey(route.RequestType.ToString(), method.ToString(), requestedRoute));
            return route.CpuMonitor;
        }

        private ITrakHoundLogger GetEndpointLogger(MethodRoute route)
        {
            // Loggers are cached by name, so the Logger is kept on the Route instead of building its name for every Request
            return route.Logger ??= CreateEndpointLogger(route);
        }

        private ITrakHoundLogger CreateEndpointLogger(MethodRoute route)
        {
            var controllerRoute = !string.IsNullOrEmpty(route.ControllerRoute) && route.ControllerRoute != "/" ? route.ControllerRoute : ":";
            var endpointRoute = !string.IsNullOrEmpty(route.EndpointRoute) && route.EndpointRoute != "/" ? route.EndpointRoute : ":";

            return _logProvider.GetLoggerByName($"api/{route.Controller.Package.Id}/{controllerRoute}/{route.RequestType}/{endpointRoute}");
        }

        private static long LogRequestParameters(ITrakHoundLogger logger, MethodRoute route, string requestId, Dictionary<string, string> queryParameters, Stream requestBody)
        {
            var debug = logger.IsEnabled(TrakHoundLogLevel.Debug);
            if (debug && !queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    // Only log the Name (values may contain sensitive information such as tokens)
                    logger.LogDebug($"Request Received : {route.RequestType} : {requestId} : Query Parameter : Name = '{queryParameter.Key}'");
                }
            }

            long requestBodyLength = 0;
            if (requestBody != null)
            {
                if (requestBody.CanSeek) requestBodyLength = requestBody.Length;
                if (debug) logger.LogDebug($"Request Received : {route.RequestType} : {requestId} : Body Parameter : Size = {requestBodyLength.ToFileSize()}");
            }

            return requestBodyLength;
        }

        private async Task<TrakHoundApiResponse> ProcessRoute(TrakHoundApiController controller, MethodInfo method, string route, string url, Dictionary<string, string> queryParameters, Dictionary<string, string> requestHeaders, Stream requestBody, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputParameters = BindParameters(method, route, url, queryParameters, requestHeaders, requestBody, cancellationToken, out var bindError);
            if (bindError != null)
            {
                return TrakHoundApiResponse.BadRequest(bindError);
            }

            if (method.ReturnType == typeof(Task<TrakHoundApiResponse>))
            {
                return await (Task<TrakHoundApiResponse>)method.Invoke(controller, inputParameters);
            }
            else
            {
                return (TrakHoundApiResponse)method.Invoke(controller, inputParameters);
            }
        }

        private async Task<ITrakHoundStream<TrakHoundApiResponse>> ProcessStreamRoute(TrakHoundApiController controller, MethodInfo method, string route, string url, Dictionary<string, string> queryParameters, Dictionary<string, string> requestHeaders, Stream requestBody, ITrakHoundLogger logger, string requestId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputParameters = BindParameters(method, route, url, queryParameters, requestHeaders, requestBody, cancellationToken, out var bindError);
            if (bindError != null)
            {
                logger.LogWarning($"Request Response : {TrakHoundApiEndpointType.Stream} : {requestId} : Bad Request : {bindError}");
                return null;
            }

            try
            {
                if (method.ReturnType == typeof(ITrakHoundStream<TrakHoundApiResponse>))
                {
                    return (ITrakHoundStream<TrakHoundApiResponse>)method.Invoke(controller, inputParameters);
                }
                else if (method.ReturnType == typeof(Task<ITrakHoundStream<TrakHoundApiResponse>>))
                {
                    return await (Task<ITrakHoundStream<TrakHoundApiResponse>>)method.Invoke(controller, inputParameters);
                }
            }
            catch (Exception ex)
            {
                var exception = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                logger.LogError($"Request Response : {TrakHoundApiEndpointType.Stream} : {requestId} : Exception : {exception.HResult} : {exception.Message} : {TrakHoundLogger.GetExceptionStackTrace(exception)}");
            }

            return null;
        }

        /// <summary>
        /// Create the Invoke arguments for the Method from the Route, Query, Header, and Body parameters.
        /// CancellationToken parameters are set to the Request's CancellationToken.
        /// </summary>
        /// <param name="error">Set when the Request Body could not be read into the Body parameter</param>
        private object[] BindParameters(MethodInfo method, string route, string url, Dictionary<string, string> queryParameters, Dictionary<string, string> requestHeaders, Stream requestBody, CancellationToken cancellationToken, out string error)
        {
            error = null;

            var methodParameters = GetMethodParameters(method);
            if (methodParameters.DefaultValues.Length < 1) return null;

            var inputParameters = (object[])methodParameters.DefaultValues.Clone();

            // CancellationToken Parameters
            foreach (var index in methodParameters.CancellationTokenIndexes)
            {
                inputParameters[index] = cancellationToken;
            }

            // Route Parameters
            if (methodParameters.RouteParameters.Count > 0)
            {
                // Extract the Route Parameters from the URL
                var routeParameters = GetRouteParameters(url, route);

                foreach (var parameterEntry in methodParameters.RouteParameters)
                {
                    var routeParameter = routeParameters.GetValueOrDefault(parameterEntry.Key);
                    if (routeParameter != null)
                    {
                        inputParameters[parameterEntry.Value.Index] = ChangeType(routeParameter.Value, parameterEntry.Value.Parameter.ParameterType);
                    }
                }
            }

            // Query Parameters
            foreach (var parameterEntry in methodParameters.QueryParameters)
            {
                var queryParameter = queryParameters?.GetValueOrDefault(parameterEntry.Key);
                if (queryParameter != null)
                {
                    inputParameters[parameterEntry.Value.Index] = ChangeType(queryParameter, parameterEntry.Value.Parameter.ParameterType);
                }
            }

            // Header Parameters (requestHeaders is case-insensitive and has no Withheld Headers, see TrakHoundApiHeaders.Filter)
            foreach (var headerParameter in methodParameters.HeaderParameters)
            {
                var headerValue = requestHeaders?.GetValueOrDefault(headerParameter.Name);
                if (headerValue != null)
                {
                    inputParameters[headerParameter.Index] = ChangeType(headerValue, headerParameter.Parameter.ParameterType);
                }
            }

            // Body Parameter
            var bodyParameter = methodParameters.BodyParameter;
            if (bodyParameter != null && requestBody != null && !(requestBody.CanSeek && requestBody.Length == 0))
            {
                var parameterType = bodyParameter.Parameter.ParameterType;

                var contentType = bodyParameter.Attribute.ContentType;
                if (string.IsNullOrEmpty(contentType)) contentType = FromBodyAttribute.GetDefaultContentType(parameterType);

                if (requestBody.CanSeek) requestBody.Seek(0, SeekOrigin.Begin);

                switch (contentType)
                {
                    case "application/json":

                        try
                        {
                            inputParameters[bodyParameter.Index] = JsonSerializer.Deserialize(requestBody, parameterType, Json.SharedOptions);
                        }
                        catch (Exception ex)
                        {
                            error = $"Invalid JSON Request Body : {ex.Message}";
                        }
                        break;

                    case "text/plain":

                        try
                        {
                            using (var reader = new StreamReader(requestBody, Encoding.UTF8, leaveOpen: true))
                            {
                                inputParameters[bodyParameter.Index] = Convert.ChangeType(reader.ReadToEnd(), parameterType);
                            }
                        }
                        catch (Exception ex)
                        {
                            error = $"Invalid Text Request Body : {ex.Message}";
                        }
                        break;

                    case "application/octet-stream":

                        if (parameterType == typeof(byte[]))
                        {
                            // Set Parameter as byte[] (the Body is usually already buffered, so it is copied once)
                            if (requestBody is MemoryStream memoryStream)
                            {
                                inputParameters[bodyParameter.Index] = memoryStream.ToArray();
                            }
                            else
                            {
                                using (var readStream = new MemoryStream())
                                {
                                    requestBody.CopyTo(readStream);
                                    inputParameters[bodyParameter.Index] = readStream.ToArray();
                                }
                            }
                        }
                        else if (parameterType == typeof(Stream))
                        {
                            // Set Parameter as Stream
                            inputParameters[bodyParameter.Index] = requestBody;
                        }
                        break;
                }
            }

            return inputParameters;
        }


        private bool TryGetRouteConfiguration(string route, out ApiRouteConfiguration routeConfiguration)
        {
            routeConfiguration = default;

            if (!string.IsNullOrEmpty(route))
            {
                var path = Url.RemoveQueryParameters(route);
                if (!string.IsNullOrEmpty(path))
                {
                    var routes = _routes;
                    if (!routes.IsNullOrEmpty())
                    {
                        // Routes are sorted descending so that more specific Routes are matched first
                        foreach (var routeConfig in routes)
                        {
                            if (!string.IsNullOrEmpty(routeConfig.Route) && IsRoutePrefix(path, routeConfig.Route))
                            {
                                routeConfiguration = routeConfig;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsRoutePrefix(string path, string prefix)
        {
            var basePath = prefix.TrimEnd('/');
            if (basePath.Length == 0) return true;

            if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return false;

            // Only match on a full segment boundary (ex. "/api" should not match "/apiv2")
            return path.Length == basePath.Length || path[basePath.Length] == '/';
        }

        private static bool IsRouteMatch(string url, MethodRoute route)
        {
            if (string.IsNullOrEmpty(url)) return string.IsNullOrEmpty(route.Route);

            var fUrl = Url.RemoveQueryParameters(url);
            return route.RouteRegex.IsMatch(fUrl) && (route.IsWildcard || CountSegments(fUrl) == route.RouteSegments);
        }

        private bool IsRouteMatch(string url, string route)
        {
            if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(route)) return true;

            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(route))
            {
                var routeRegex = _routeRegexes.GetOrAdd(route, o => new Regex(CreateRouteRegexPattern(o), RegexOptions.Compiled));
                var fUrl = Url.RemoveQueryParameters(url);

                return routeRegex.IsMatch(fUrl) && (route.Contains('*') || CountSegments(fUrl) == CountSegments(route));
            }

            return false;
        }

        // The number of '/' separated parts (the same as Split('/').Length, without allocating)
        private static int CountSegments(string route) => route.AsSpan().Count('/') + 1;

        private static string CreateRouteRegexPattern(string route)
        {
            var pattern = new StringBuilder("^");

            if (!string.IsNullOrEmpty(route))
            {
                // Escape the literal parts of the Route and replace each Route Parameter with a wildcard
                var index = 0;
                foreach (Match match in _routeParameterRegex.Matches(route))
                {
                    pattern.Append(Regex.Escape(route.Substring(index, match.Index - index)));
                    pattern.Append(".*");
                    index = match.Index + match.Length;
                }

                pattern.Append(Regex.Escape(route.Substring(index)));
            }

            pattern.Append('$');
            return pattern.ToString();
        }

        private static Dictionary<string, RouteParameter> GetRouteParameters(string url, string route)
        {
            var outputParameters = new Dictionary<string, RouteParameter>();

            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(route))
            {
                var urlParts = url.Split('/');
                var routeParts = route.Split('/');

                for (var i = 0; i < urlParts.Length && i < routeParts.Length; i++)
                {
                    var match = _routeParameterRegex.Match(routeParts[i]);
                    if (match.Success)
                    {
                        var parameterName = match.Groups[2].ToString().TrimStart('*');
                        var parameterKey = parameterName.ToLower();

                        if (!outputParameters.ContainsKey(parameterKey))
                        {
                            var parameterValue = Url.GetRouteParameter(url, route, parameterName);
                            outputParameters.Add(parameterKey, new RouteParameter(parameterKey, parameterValue));
                        }
                    }
                }
            }

            return outputParameters;
        }

        private static object ChangeType(object obj, Type type)
        {
            if (obj != null)
            {
                var convertType = type;

                if (type.IsValueType && Nullable.GetUnderlyingType(type) != null)
                {
                    convertType = Nullable.GetUnderlyingType(type);
                }

                try
                {
                    if (convertType == typeof(DateTime))
                    {
                        return obj.ToString().ToDateTime();
                    }
                    else if (typeof(Enum).IsAssignableFrom(convertType) && obj.GetType() == typeof(string))
                    {
                        if (Enum.TryParse(convertType, (string)obj, true, out var result))
                        {
                            return result;
                        }
                    }
                    else
                    {
                        return Convert.ChangeType(obj, convertType);
                    }
                }
                catch { }
            }

            return default;
        }

        #endregion


        private async void LoadDelayElapsed(object sender, EventArgs args)
        {
            try
            {
                await Load();
            }
            catch (Exception ex)
            {
                _logger.LogError($"API Load Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        public async Task Load()
        {
            await _loadLock.WaitAsync();
            try
            {
                await LoadConfigurations();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async Task LoadConfigurations()
        {
            var foundRoutes = new HashSet<string>();
            var loads = new List<(string Route, TrakHoundApiConfiguration Configuration, ITrakHoundModule Module, string LoadHash)>();

            var configurations = _configurationProfile.Get<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations.ToList())
                {
                    if (!string.IsNullOrEmpty(configuration.Route))
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

                            var route = RewriteRoute(configuration.Route);
                            if (!route.StartsWith('/')) route = '/' + route;

                            // Read before the Api is loaded, so a Group change during the Load reloads it again (instead of being missed)
                            var loadHash = GetLoadHash(configuration);

                            if (loadHash != installedConfigurationHash || module.Package.Hash != installedModuleHash)
                            {
                                loads.Add((route, configuration, module, loadHash));
                            }

                            foundRoutes.Add(route);
                        }
                        else
                        {
                            // Module Not Found
                            _logger.LogError($"API Load Error : Module Not Found : Package ID = {configuration.PackageId}");
                        }
                    }
                }
            }

            // Apis are loaded in parallel (so a slow InitializeAsync doesn't delay the others). Configurations for the same Route
            // are still loaded in order, so the last one wins as before
            using (var throttle = new SemaphoreSlim(_maxParallelLoads))
            {
                await Task.WhenAll(loads.GroupBy(o => o.Route).Select(async group =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        foreach (var load in group)
                        {
                            await LoadConfiguration(load.Configuration, load.Route, load.Module);
                            SetInstalledHashes(load.Configuration, load.Module, load.LoadHash);
                        }
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }));
            }

            // Remove unused Routes
            var removedRoutes = new List<ApiRouteConfiguration>();
            lock (_lock)
            {
                if (!_routes.IsNullOrEmpty())
                {
                    removedRoutes.AddRange(_routes.Where(o => !foundRoutes.Contains(o.Route)));
                    if (removedRoutes.Count > 0)
                    {
                        _routes = _routes.Where(o => foundRoutes.Contains(o.Route)).OrderByDescending(o => o.Route).ToArray();
                    }
                }
            }

            foreach (var removedRoute in removedRoutes)
            {
                UnloadRoute(removedRoute);
            }
        }

        /// <summary>
        /// The Configuration's Hash and the Router it resolves to (so the Api reloads when its Group's Router changes)
        /// </summary>
        private string GetLoadHash(TrakHoundApiConfiguration configuration)
        {
            return TrakHoundGroups.GetLoadHash(_configurationProfile, configuration.Hash, configuration.RouterId, configuration.GroupId);
        }

        private void SetInstalledHashes(TrakHoundApiConfiguration configuration, ITrakHoundModule module, string loadHash)
        {
            lock (_lock)
            {
                _installedConfigurationHashes[configuration.Id] = loadHash;
                _installedModuleHashes[configuration.Id] = module.Package.Hash;
            }
        }

        private async Task LoadConfiguration(TrakHoundApiConfiguration configuration, string route, ITrakHoundModule module)
        {
            if (configuration != null && module != null && !module.ModuleTypes.IsNullOrEmpty())
            {
                var apiLogger = _logProvider.GetLoggerByName(TrakHoundPath.Combine("api", module.Package.Id));
                apiLogger.LogInformation($"Loading API Configuration : {configuration.Id} : {module.PackageId} : {route}");

                var client = _clientProvider.GetClient(TrakHoundGroups.GetRouterId(_configurationProfile, configuration.RouterId, configuration.GroupId))
                    .WithRequestSource(TrakHoundApiRequestSource.Create(TrakHoundApiRequestSourceType.Api, configuration.Id, configuration.Name, configuration.PackageId));

                var volumeId = !string.IsNullOrEmpty(configuration.VolumeId) ? configuration.VolumeId : configuration.Id;
                var volume = _volumeProvider.GetVolume(volumeId);

                var variables = _variableProvider.GetCollection(configuration.Id);

                var controllers = new List<TrakHoundApiController>();

                if (client != null && volume != null)
                {
                    foreach (var moduleType in module.ModuleTypes)
                    {
                        var controller = CreateInstance(module, moduleType);
                        if (controller != null)
                        {
                            var controllerInformation = TrakHoundApiInformation.CreateControllerInformation(configuration, moduleType);

                            var controllerRoute = !string.IsNullOrEmpty(controllerInformation.Route) && controllerInformation.Route != "/" ? controllerInformation.Route : ":";
                            if (!controllerRoute.StartsWith('/')) controllerRoute = '/' + controllerRoute;

                            // Loggers are cached by name, so remove the handler before adding to prevent duplicates on reload
                            var controllerLogger = _logProvider.GetLoggerByName(TrakHoundPath.Combine("api", module.Package.Id, controllerRoute));
                            controllerLogger.LogEntryReceived -= LogEntryReceived;
                            controllerLogger.LogEntryReceived += LogEntryReceived;

                            controller.Id = configuration.Id;
                            controller.InstanceId = _instance.Id;
                            controller.BaseUrl = _baseUrl;
                            controller.BasePath = controllerRoute;
                            controller.BaseLocation = $"{_packagesDirectory}/{module.PackageId}/{module.PackageVersion}";
                            controller.SetConfiguration(configuration);
                            controller.SetPackage(module.Package);
                            controller.SetClient(client);
                            controller.SetVolume(volume);
                            controller.SetVariables(variables);
                            controller.SetLogger(controllerLogger);

                            try
                            {
                                controller.Initialize();
                                await controller.InitializeAsync();
                            }
                            catch (Exception ex)
                            {
                                controllerLogger.LogError($"API Controller Initialization Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
                            }

                            controllers.Add(controller);
                        }
                        else
                        {
                            _logger.LogError($"API Controller Load Error : {configuration.Id} : {module.PackageId}");
                        }
                    }
                }
                else
                {
                    if (client == null) _logger.LogError($"API Load Error : Client Not Found : {configuration.Id} : Router ID = {configuration.RouterId}");
                    if (volume == null) _logger.LogError($"API Load Error : Volume Not Found : {configuration.Id} : Volume ID = {volumeId}");
                }

                var routeConfiguration = CreateRouteConfiguration(new ApiRouteConfiguration(route, module.Package, configuration, controllers, volumeId, module.PackageReadMe));
                var replacedRoutes = new List<ApiRouteConfiguration>();

                lock (_lock)
                {
                    var routes = _routes?.ToList() ?? new List<ApiRouteConfiguration>();
                    replacedRoutes.AddRange(routes.Where(o => o.Route == route));
                    routes.RemoveAll(o => o.Route == route);
                    routes.Add(routeConfiguration);
                    _routes = routes.OrderByDescending(o => o.Route).ToArray();
                }

                // Unload the previous Controllers (after the new Route is in place)
                foreach (var replacedRoute in replacedRoutes)
                {
                    UnloadRoute(replacedRoute);
                }

                var apiInformation = routeConfiguration.Information;
                if (apiInformation != null)
                {
                    // Add Security Resources
                    foreach (var (resourceId, permissions) in GetSecurityResources(apiInformation))
                    {
                        if (!permissions.IsNullOrEmpty())
                        {
                            _instance.SecurityManager.AddResource(Security.TrakHoundIdentityResourceType.Api, resourceId, permissions);
                        }
                        else
                        {
                            _instance.SecurityManager.AddResource(Security.TrakHoundIdentityResourceType.Api, resourceId);
                        }
                    }

                    ApiAdded?.Invoke(this, apiInformation);
                }
            }
        }

        /// <summary>
        /// Builds what every Request reads (the Api Information, and the sorted Endpoint Routes linked to their Endpoint Information) once, when the Route is loaded
        /// </summary>
        private ApiRouteConfiguration CreateRouteConfiguration(ApiRouteConfiguration routeConfiguration)
        {
            var information = CreateInformation(routeConfiguration);
            routeConfiguration.Information = information;
            routeConfiguration.Key = $"{routeConfiguration.Configuration?.Hash}:{routeConfiguration.Package?.Hash}";
            routeConfiguration.EndpointRoutes = CreateEndpointRoutes(information);

            var methodRoutes = GetRoutes(routeConfiguration.Controllers)?.OrderBy(o => o.Route, _routeComparer).ToArray() ?? Array.Empty<MethodRoute>();
            routeConfiguration.MethodRoutes = methodRoutes;

            if (information != null && routeConfiguration.Configuration != null)
            {
                foreach (var methodRoute in methodRoutes)
                {
                    var controllerId = TrakHoundApiInformation.GetControllerId(routeConfiguration.Configuration.Id, methodRoute.Controller.GetType());
                    var endpointId = TrakHoundApiInformation.GetEndpointId(methodRoute.RequestMethod, methodRoute.EndpointRoute);

                    var controllerInformation = information.Controllers?.FirstOrDefault(o => o?.Id == controllerId);
                    var endpointInformation = controllerInformation?.EndPoints?.FirstOrDefault(o => o.Id == endpointId);
                    if (endpointInformation != null)
                    {
                        endpointInformation.Api = information;
                        endpointInformation.Controller = controllerInformation;
                    }

                    methodRoute.EndpointInformation = endpointInformation;
                }
            }

            return routeConfiguration;
        }

        private void UnloadRoute(ApiRouteConfiguration routeConfiguration)
        {
            DisposeControllers(routeConfiguration.Controllers);

            var configurationId = routeConfiguration.Configuration?.Id;
            if (!string.IsNullOrEmpty(configurationId))
            {
                bool isLoaded;
                lock (_lock) isLoaded = _routes?.Any(o => o.Configuration?.Id == configurationId) == true;

                // The Configuration may still be loaded under a different Route (ex. Route changed or Configuration reloaded)
                if (!isLoaded)
                {
                    lock (_lock)
                    {
                        // Remove the Installed Hashes so that the Configuration is loaded again if it is added back
                        _installedConfigurationHashes.Remove(configurationId);
                        _installedModuleHashes.Remove(configurationId);
                    }

                    // Remove Security Resources
                    var apiInformation = routeConfiguration.Information;
                    if (apiInformation != null)
                    {
                        foreach (var (resourceId, _) in GetSecurityResources(apiInformation))
                        {
                            _instance.SecurityManager.RemoveResource(resourceId);
                        }
                    }

                    ApiRemoved?.Invoke(this, configurationId);
                }
            }
        }

        private void DisposeControllers(IEnumerable<TrakHoundApiController> controllers)
        {
            if (!controllers.IsNullOrEmpty())
            {
                foreach (var controller in controllers)
                {
                    if (controller is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"API Controller Dispose Error : {controller.Id} : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
                        }
                    }
                }
            }
        }

        private static IEnumerable<(string ResourceId, IEnumerable<string> Permissions)> GetSecurityResources(TrakHoundApiInformation apiInformation)
        {
            if (!apiInformation.Controllers.IsNullOrEmpty())
            {
                foreach (var controllerInformation in apiInformation.Controllers)
                {
                    if (!controllerInformation.EndPoints.IsNullOrEmpty())
                    {
                        foreach (var endpointInformation in controllerInformation.EndPoints)
                        {
                            yield return (TrakHoundApiInformation.GetEndpointResourceId(apiInformation.Id, controllerInformation.Id, endpointInformation.Id), endpointInformation.Permissions);
                        }
                    }
                }
            }
        }

        private void LogEntryReceived(object sender, TrakHoundLogItem item)
        {
            if (sender is ITrakHoundLogger logger)
            {
                ApiLogUpdated?.Invoke(logger.Id, item);
            }
        }

        private TrakHoundApiController CreateInstance(ITrakHoundModule module, Type type)
        {
            if (type != null)
            {
                try
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        return (TrakHoundApiController)constructor.Invoke(null);
                    }
                }
                catch (Exception ex)
                {
                    ApiLoadError?.Invoke(this, ex);
                }
            }

            return null;
        }

        private string RewriteRoute(string route)
        {
            if (route == null) return null;

            if (!route.StartsWith('/')) route = '/' + route;

            // Rewrite Route (when matched)
            if (!_instance.Configuration.RouteRewrites.IsNullOrEmpty())
            {
                foreach (var routeRewrite in _instance.Configuration.RouteRewrites)
                {
                    route = Url.Rewrite(route, routeRewrite.Key, routeRewrite.Value);
                }
            }

            return route;
        }
    }
}
