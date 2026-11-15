// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Routing;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Security;
using TrakHound.Services;

namespace TrakHound.Instance.Pages.Security
{
    public enum PublicEndpointStatus
    {
        /// <summary>
        /// Anyone who can reach the Instance can use it without signing in
        /// </summary>
        Public,

        /// <summary>
        /// Some operations are Public (ex. a Service with only some operations protected)
        /// </summary>
        Partial,

        /// <summary>
        /// Not covered by a Profile, so it can only be used from the same machine as the Instance (see TrakHoundSystemApiProtection)
        /// </summary>
        LocalOnly,

        /// <summary>
        /// An Identity is required
        /// </summary>
        SignIn,

        /// <summary>
        /// Not served over HTTP (disabled in Settings, or the route is not served)
        /// </summary>
        NotServed
    }

    public static class PublicEndpointAreas
    {
        public const string Apis = "Apis";
        public const string Apps = "Apps";
        public const string SystemApi = "System API";
        public const string Services = "Services";
        public const string Admin = "Admin Interface";
        public const string InstancePages = "Instance Pages";
        public const string StaticFiles = "Static Files";

        public static readonly string[] All = new[] { Apis, Apps, SystemApi, Services, Admin, InstancePages, StaticFiles };

        public static string GetIcon(string area)
        {
            switch (area)
            {
                case Apis: return "fa-solid fa-sharp fa-circle-location-arrow";
                case Apps: return "fa-solid fa-sharp fa-browsers";
                case SystemApi: return "fa-solid fa-sharp fa-gear";
                case Services: return "fa-solid fa-sharp fa-right-left-large";
                case Admin: return "fa-solid fa-sharp fa-screwdriver-wrench";
                case InstancePages: return "fa-solid fa-sharp fa-file";
                default: return "fa-solid fa-sharp fa-file-code";
            }
        }
    }

    /// <summary>
    /// An endpoint (or group of endpoints) served over HTTP and whether it can be used without signing in
    /// </summary>
    public class PublicEndpoint
    {
        public string Area { get; set; }

        public string Route { get; set; }

        public string Methods { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public PublicEndpointStatus Status { get; set; }

        /// <summary>
        /// Why the endpoint has its Status (ex. "No Profile covers this Resource")
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// The Security Resource that controls access (null when access is not controlled by a Profile)
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// Public on purpose (ex. Health checks and the files needed to load pages). These can't be protected with a Profile
        /// </summary>
        public bool ByDesign { get; set; }

        /// <summary>
        /// Public because of a setting that is usually not intended (ex. Admin Authentication disabled)
        /// </summary>
        public bool Warning { get; set; }

        /// <summary>
        /// The Port the endpoint is served on when there is a separate Admin Port (null when served on every Port)
        /// </summary>
        public int? Port { get; set; }

        public bool IsPublic => Status == PublicEndpointStatus.Public || Status == PublicEndpointStatus.Partial;
    }


    /// <summary>
    /// Lists every endpoint served over HTTP and evaluates it for an anonymous Request (using the current Settings, Profiles, and Assignments)
    /// </summary>
    public class PublicEndpointsReport
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundInstanceConfiguration _configuration;
        private readonly ITrakHoundSecurityManager _securityManager;
        private readonly SecurityState _state;
        private readonly SecurityResources _resources;
        private readonly List<PublicEndpoint> _endpoints = new List<PublicEndpoint>();


        public IReadOnlyList<PublicEndpoint> Endpoints => _endpoints;

        public TrakHoundInstanceConfiguration Configuration => _configuration;


        private PublicEndpointsReport(ITrakHoundInstance instance, TrakHoundInstanceConfiguration configuration)
        {
            _instance = instance;
            _configuration = configuration ?? instance.Configuration ?? new TrakHoundInstanceConfiguration();
            _securityManager = instance.SecurityManager;
            _state = SecurityState.Load(instance);
            _resources = new SecurityResources(instance);
        }

        public static PublicEndpointsReport Create(ITrakHoundInstance instance, TrakHoundInstanceConfiguration configuration, EndpointDataSource endpointDataSource)
        {
            var report = new PublicEndpointsReport(instance, configuration);
            report.AddApis();
            report.AddApps();
            report.AddSystemApi(endpointDataSource);
            report.AddServices();
            report.AddAdmin();
            report.AddInstancePages();
            report.AddStaticFiles();
            report.SetPorts();
            return report;
        }


        private void AddApis()
        {
            foreach (var api in _resources.Apis)
            {
                if (api.Controllers == null) continue;

                foreach (var controller in api.Controllers.Where(o => o.EndPoints != null))
                {
                    foreach (var endpoint in controller.EndPoints)
                    {
                        var item = new PublicEndpoint
                        {
                            Area = PublicEndpointAreas.Apis,
                            Route = CombineRoute(api.Route, controller.Route, endpoint.Route),
                            Methods = endpoint.Method?.ToString().ToUpperInvariant() ?? endpoint.Type.ToString().ToUpperInvariant(),
                            Title = api.PackageId,
                            Description = endpoint.Description,
                            ResourceId = $"api:{api.Id}:{controller.Id}:{endpoint.Id}"
                        };

                        if (!_configuration.ApiHttpEnabled) SetNotServed(item, "Apis are not served over HTTP (Settings > Interfaces)");
                        else if (!api.HttpEnabled) SetNotServed(item, "This Api is not served over HTTP (its Configuration)");
                        else Evaluate(item);

                        _endpoints.Add(item);
                    }
                }
            }
        }

        private void AddApps()
        {
            foreach (var app in _resources.Apps)
            {
                if (app.Pages == null) continue;

                foreach (var page in app.Pages)
                {
                    var item = new PublicEndpoint
                    {
                        Area = PublicEndpointAreas.Apps,
                        Route = CombineRoute(app.Route, page.Route),
                        Methods = "GET",
                        Title = app.Name ?? app.PackageId,
                        ResourceId = $"app:{app.Id}:{page.Id}"
                    };

                    if (!_configuration.AppsHttpEnabled) SetNotServed(item, "Apps are not served over HTTP (Settings > Interfaces)");
                    else Evaluate(item);

                    _endpoints.Add(item);
                }
            }
        }

        private void AddSystemApi(EndpointDataSource endpointDataSource)
        {
            if (endpointDataSource == null) return;

            foreach (var endpoint in endpointDataSource.Endpoints.OfType<RouteEndpoint>())
            {
                var route = "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/');
                var developerRoute = TrakHoundInstanceRoutes.IsDeveloperRoute(route);
                if (!TrakHoundInstanceRoutes.IsSystemApiRoute(route) && !route.StartsWith("/_workers") && !developerRoute) continue;

                var metadata = endpoint.Metadata;

                // Only Controller Actions (conventional routes also add endpoints that are only used for link generation)
                if (metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() == null) continue;
                if (metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching == true) continue;

                // Service operations are listed per Service (see AddServices)
                if (metadata.GetMetadata<TrakHoundServiceOperationsAttribute>() != null) continue;

                var methods = metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                var item = new PublicEndpoint
                {
                    Area = PublicEndpointAreas.SystemApi,
                    Route = route,
                    Methods = methods != null && methods.Count > 0 ? string.Join(", ", methods) : "ANY"
                };

                var resource = metadata.GetMetadata<TrakHoundSystemResourceAttribute>();
                var write = metadata.GetMetadata<TrakHoundSystemWriteAttribute>() != null;
                var systemResource = resource != null ? TrakHoundSystemSecurity.GetResource(resource.ResourceId) : null;
                item.Title = systemResource?.Title ?? "Unclassified";
                item.Description = write ? "Changes the Instance (requires the write Permission)" : null;
                item.ResourceId = resource?.ResourceId;

                if (developerRoute)
                {
                    // The Api Catalog is served with the Apis (not by the System API)
                    if (!_configuration.ApiHttpEnabled) SetNotServed(item, "Apis are not served over HTTP (Settings > Interfaces)");
                    else Evaluate(item, TrakHoundSystemSecurity.GetPermission(write));
                }
                else if (!_configuration.SystemApiEnabled)
                {
                    SetNotServed(item, "The System API is disabled (Settings > Interfaces)");
                }
                else if (!TrakHoundInstanceRoutes.IsSystemApiRoute(route))
                {
                    SetNotServed(item, "The route is not in the Instance's System API route list, so requests return 404");
                }
                else if (resource == null)
                {
                    // A System API Controller without a TrakHoundSystemResourceAttribute can't be protected
                    item.Status = PublicEndpointStatus.Public;
                    item.Reason = "Not covered by a System Resource, so it can't be protected by a Profile";
                    item.Warning = true;
                }
                else
                {
                    Evaluate(item, TrakHoundSystemSecurity.GetPermission(write), true);
                }

                _endpoints.Add(item);
            }
        }

        private void AddServices()
        {
            var services = _instance.ServicesProvider?.Drivers?.Services?.ToList() ?? new List<ITrakHoundService>();
            foreach (var service in services.OrderBy(o => o.Configuration?.Name ?? o.Configuration?.Id))
            {
                var configurationId = service.Configuration?.Id;
                if (string.IsNullOrEmpty(configurationId)) continue;

                foreach (var family in TrakHoundServiceSecurity.GetFamilies(service.GetType()))
                {
                    var item = new PublicEndpoint
                    {
                        Area = PublicEndpointAreas.Services,
                        Route = $"/_services/{family}/*",
                        Methods = "GET, POST, PUT, DELETE",
                        Title = !string.IsNullOrEmpty(service.Configuration.Name) ? service.Configuration.Name : configurationId,
                        Description = $"{SecurityResources.GetFamilyTitle(family)} Operations",
                        ResourceId = $"{TrakHoundServiceSecurity.ResourcePrefix}:{configurationId}:{family}:*"
                    };

                    if (!_configuration.SystemApiEnabled)
                    {
                        SetNotServed(item, "Services are served by the System API, which is disabled (Settings > Interfaces)");
                    }
                    else
                    {
                        // Each operation is authorized separately (with its read or write Permission)
                        var operations = SecurityResources.GetServiceOperations(family).Select(o => o.Value).ToList();
                        var authorizations = operations.Select(operation =>
                        {
                            var resourceId = TrakHoundServiceSecurity.GetResourceId(configurationId, family, operation);
                            return (Operation: operation, Authorization: _securityManager.Authorize(null, resourceId, TrakHoundServiceSecurity.GetPermission(operation)));
                        }).ToList();
                        var publicOperations = authorizations.Where(o => o.Authorization.IsAllowed && !IsLocalOnly(o.Authorization, true)).Select(o => o.Operation).ToList();

                        if (publicOperations.Count == operations.Count)
                        {
                            Evaluate(item, TrakHoundServiceSecurity.GetPermission(operations.FirstOrDefault()));
                            item.Status = PublicEndpointStatus.Public;
                        }
                        else if (publicOperations.Count > 0)
                        {
                            item.Status = PublicEndpointStatus.Partial;
                            item.Reason = $"Public operations : {string.Join(", ", publicOperations)}";
                        }
                        else if (authorizations.Any(o => IsLocalOnly(o.Authorization, true)))
                        {
                            item.Status = PublicEndpointStatus.LocalOnly;
                            item.Reason = LocalOnlyReason;
                        }
                        else
                        {
                            Evaluate(item, TrakHoundServiceSecurity.GetPermission(operations.FirstOrDefault()));
                            item.Status = PublicEndpointStatus.SignIn;
                        }
                    }

                    _endpoints.Add(item);
                }
            }

            // Availability checks don't read or write data, so they are always allowed
            _endpoints.Add(new PublicEndpoint
            {
                Area = PublicEndpointAreas.Services,
                Route = "/_services/{type}/availability",
                Methods = "GET",
                Title = "Availability",
                Description = "Whether a Service is available (no data is read or written)",
                Status = _configuration.SystemApiEnabled ? PublicEndpointStatus.Public : PublicEndpointStatus.NotServed,
                Reason = _configuration.SystemApiEnabled ? "Always allowed" : "Services are served by the System API, which is disabled (Settings > Interfaces)",
                ByDesign = _configuration.SystemApiEnabled
            });
        }

        private void AddAdmin()
        {
            var item = new PublicEndpoint
            {
                Area = PublicEndpointAreas.Admin,
                Route = $"{TrakHoundInstanceRoutes.AdminPrefix}/*",
                Methods = "GET",
                Title = "Admin Interface",
                Description = "Explorer, Packages, Settings, Security, ..."
            };

            if (!_configuration.AdminInterfaceEnabled)
            {
                SetNotServed(item, "The Admin Interface is disabled");
            }
            else if (_configuration.AdminAuthenticationEnabled)
            {
                item.Status = PublicEndpointStatus.SignIn;
                item.Reason = "Admin sign in (Admin Authentication is enabled)";
            }
            else
            {
                item.Status = PublicEndpointStatus.Public;
                item.Reason = "Admin Authentication is disabled, so anyone can manage this Instance";
                item.Warning = true;
            }

            _endpoints.Add(item);
        }

        private void AddInstancePages()
        {
            AddByDesign(PublicEndpointAreas.InstancePages, "/_health", "GET", "Health", "The overall Health status for Load Balancers and Container health checks (no details)");
            AddByDesign(PublicEndpointAreas.InstancePages, "/_health/live", "GET", "Liveness", "Whether the Instance process is running");

            var init = AddByDesign(PublicEndpointAreas.InstancePages, "/" + TrakHoundInitializationPage.Route, "GET", "Initialization Page", "Shown while the Instance is starting, restarting, or stopped");
            if (!_configuration.InitializationPageEnabled) SetNotServed(init, "The Initialization Page is disabled (Settings > Interfaces)");

            var initAssets = AddByDesign(PublicEndpointAreas.InstancePages, $"/{TrakHoundInitializationPage.Route}/{TrakHoundInitializationPage.AssetsRoute}/*", "GET", "Initialization Page Files", "The files of the Custom Initialization Page (ex. images and stylesheets)");
            if (!_configuration.InitializationPageEnabled) SetNotServed(initAssets, "The Initialization Page is disabled (Settings > Interfaces)");
            else if (string.IsNullOrWhiteSpace(_configuration.InitializationPagePath)) SetNotServed(initAssets, "No Custom Initialization Page is set (Settings > Interfaces)");

            var error = AddByDesign(PublicEndpointAreas.InstancePages, TrakHoundInstanceRoutes.ErrorPrefix, "GET", "Error Page", "Shown for unhandled errors and Admin pages that are not found");
            if (!_configuration.ErrorPageEnabled) SetNotServed(error, "The Error Page is disabled (Settings > Interfaces)");

            AddByDesign(PublicEndpointAreas.InstancePages, $"{TrakHoundIdentityHttp.IdentityPrefix}/*", "GET, POST", "Sign In", "Sign in, sign in callbacks, and sign out for Identity Providers");
            AddByDesign(PublicEndpointAreas.InstancePages, $"{TrakHoundInstanceRoutes.DownloadRoute.Prefix}/{{key}}", "GET", TrakHoundInstanceRoutes.DownloadRoute.Title, TrakHoundInstanceRoutes.DownloadRoute.Description);
        }

        private void AddStaticFiles()
        {
            foreach (var route in TrakHoundInstanceRoutes.StaticRoutes)
            {
                AddByDesign(PublicEndpointAreas.StaticFiles, route.Prefix == "/TrakHound.Instance.styles.css" ? route.Prefix : $"{route.Prefix}/*", "GET", route.Title, route.Description);
            }
        }


        /// <summary>
        /// With a separate Admin Port, the Admin Interface and the System API (including Services and Downloads) are only served on the
        /// Admin Port, and Apps, Apis, Sign In, and the Initialization Page only on the HTTP Port
        /// </summary>
        private void SetPorts()
        {
            if (!TrakHoundAdminPort.IsEnabled) return;

            foreach (var item in _endpoints)
            {
                switch (item.Area)
                {
                    case PublicEndpointAreas.Admin:
                    case PublicEndpointAreas.SystemApi:
                    case PublicEndpointAreas.Services:
                        item.Port = TrakHoundAdminPort.Port;
                        break;

                    case PublicEndpointAreas.Apis:
                    case PublicEndpointAreas.Apps:
                        item.Port = TrakHoundAdminPort.HttpPort;
                        break;

                    case PublicEndpointAreas.InstancePages:
                        if (item.Route.StartsWith(TrakHoundInstanceRoutes.DownloadRoute.Prefix)) item.Port = TrakHoundAdminPort.Port;
                        else if (item.Route.StartsWith(TrakHoundIdentityHttp.IdentityPrefix) || item.Route.StartsWith("/" + TrakHoundInitializationPage.Route)) item.Port = TrakHoundAdminPort.HttpPort;
                        break;
                }
            }
        }

        private PublicEndpoint AddByDesign(string area, string route, string methods, string title, string description)
        {
            var item = new PublicEndpoint
            {
                Area = area,
                Route = route,
                Methods = methods,
                Title = title,
                Description = description,
                Status = PublicEndpointStatus.Public,
                Reason = "Public by design",
                ByDesign = true
            };

            _endpoints.Add(item);
            return item;
        }

        private static void SetNotServed(PublicEndpoint item, string reason)
        {
            item.Status = PublicEndpointStatus.NotServed;
            item.Reason = reason;
            item.ByDesign = false;
        }

        /// <summary>
        /// Evaluates the endpoint's Resource for an anonymous Request
        /// </summary>
        private const string LocalOnlyReason = "No Profile covers this Resource, so it can only be used from the same machine as the Instance";

        /// <summary>
        /// Whether an anonymous Request is only allowed from the same machine (System Resources and Service operations that no Profile covers, see TrakHoundSystemApiProtection)
        /// </summary>
        private bool IsLocalOnly(TrakHoundAuthorizationResult authorization, bool systemApi)
        {
            return systemApi && authorization.IsAllowed && authorization.IsUncovered && !_configuration.SystemApiUnprotectedRemoteAccess;
        }

        private void Evaluate(PublicEndpoint item, string permission = null, bool systemApi = false)
        {
            var authorization = _securityManager.Authorize(null, item.ResourceId, permission);
            if (IsLocalOnly(authorization, systemApi))
            {
                item.Status = PublicEndpointStatus.LocalOnly;
                item.Reason = LocalOnlyReason;
            }
            else if (authorization.IsAllowed)
            {
                item.Status = PublicEndpointStatus.Public;

                var profiles = (_securityManager as TrakHoundSecurityManager)?.GetProfilesByResourceId(item.ResourceId)?.ToList();
                var openProfile = profiles?.FirstOrDefault(o => string.IsNullOrEmpty(o.ProviderId));
                item.Reason = openProfile != null
                    ? $"The \"{openProfile.Id}\" Profile has no Identity Provider"
                    : "No Profile covers this Resource";
            }
            else
            {
                item.Status = PublicEndpointStatus.SignIn;

                var providers = authorization.ProviderIds.Select(o => _state.GetProvider(o)?.DisplayName ?? o).ToList();
                item.Reason = providers.Count > 0 ? $"Sign in with {string.Join(", ", providers)}" : "Requires an Identity";
            }
        }

        private static string CombineRoute(params string[] routes) => "/" + Url.Combine(routes);
    }
}
