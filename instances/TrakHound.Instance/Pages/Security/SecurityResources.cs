// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Instance.Pages.Security
{
    /// <summary>
    /// A readable description of a Security Resource ID or Assignment pattern (ex. "api:{apiId}:*" => "All Endpoints" of an Api)
    /// </summary>
    public class SecurityResourceDescription
    {
        public SecurityResourceScope Scope { get; set; }

        public string Icon { get; set; }

        public string Title { get; set; }

        public string Subtitle { get; set; }
    }

    public enum SecurityResourceScope
    {
        Everything,
        Api,
        App,
        Service,
        System,
        Custom
    }

    /// <summary>
    /// An item that can be selected in a Resource picker
    /// </summary>
    public class SecurityResourceOption
    {
        public string Value { get; set; }

        public string Text { get; set; }
    }

    /// <summary>
    /// Describes Security Resources using the Instance's loaded Api, App, and Service information, and lists the options for Resource pickers
    /// </summary>
    public class SecurityResources
    {
        public const string AllOption = "*";

        public static readonly string[] ServiceFamilies = new[] { "blobs", "key-values", "message-queues", "messages", "tables", "time-series" };

        private static readonly Dictionary<string, string[]> _serviceOperations = new Dictionary<string, string[]>
        {
            ["blobs"] = new[] { "ListCollections", "ListKeys", "List", "Read", "Download", "Upload", "Delete" },
            ["key-values"] = new[] { "ListCollections", "ListKeys", "Read", "Count", "Publish", "Delete" },
            ["message-queues"] = new[] { "Subscribe", "Pull", "Acknowledge", "Reject", "Publish", "GetMetrics" },
            ["time-series"] = new[] { "ListMeasurements", "ListSeries", "ListFields", "Read", "ReadLatest", "Aggregate", "Write", "Delete", "CheckAvailability" },
            ["messages"] = new[] { "Subscribe", "Publish" },
            ["tables"] = new[] { "List", "GetColumns", "ExecuteQuery", "ExecuteNonQuery" }
        };

        private readonly List<TrakHoundApiInformation> _apis;
        private readonly List<TrakHoundAppInformation> _apps;
        private readonly List<TrakHoundServiceConfiguration> _services;


        public IReadOnlyList<TrakHoundApiInformation> Apis => _apis;

        public IReadOnlyList<TrakHoundAppInformation> Apps => _apps;

        public IReadOnlyList<TrakHoundServiceConfiguration> Services => _services;


        public SecurityResources(ITrakHoundInstance instance)
        {
            _apis = instance.ApiProvider?.GetRouteInformation()?.OrderBy(o => o.Route).ToList() ?? new List<TrakHoundApiInformation>();
            _apps = instance.AppProvider?.GetInformation()?.OrderBy(o => o.Route).ToList() ?? new List<TrakHoundAppInformation>();
            _services = instance.ConfigurationProfile.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory)?.OrderBy(o => GetServiceName(o)).ToList() ?? new List<TrakHoundServiceConfiguration>();
        }


        public SecurityResourceDescription Describe(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return Create(SecurityResourceScope.Custom, "(none)", null);
            if (pattern == "*") return Create(SecurityResourceScope.Everything, "Everything", "All Apis, Apps, Services, and the System API");

            var parts = pattern.Split(':');
            switch (parts[0])
            {
                case "api":
                    if (parts.Length < 2 || parts[1] == "*") return Create(SecurityResourceScope.Api, "All Apis", null);
                    var api = _apis.FirstOrDefault(o => o.Id == parts[1]);
                    var apiName = api != null ? api.PackageId : parts[1];
                    if (parts.Length < 3 || parts[2] == "*") return Create(SecurityResourceScope.Api, apiName, $"All Endpoints{(api != null ? $" · {api.Route}" : "")}");
                    var endpoint = FindEndpoint(api, parts[2], parts.Length > 3 ? parts[3] : null);
                    return Create(SecurityResourceScope.Api, apiName, endpoint ?? "Endpoint (not loaded)");

                case "app":
                    if (parts.Length < 2 || parts[1] == "*") return Create(SecurityResourceScope.App, "All Apps", null);
                    var app = _apps.FirstOrDefault(o => o.Id == parts[1]);
                    var appName = app != null ? (app.Name ?? app.PackageId) : parts[1];
                    if (parts.Length < 3 || parts[2] == "*") return Create(SecurityResourceScope.App, appName, $"All Pages{(app != null ? $" · {app.Route}" : "")}");
                    var page = app?.Pages?.FirstOrDefault(o => o.Id == parts[2]);
                    return Create(SecurityResourceScope.App, appName, page != null ? $"Page · {CombineRoute(app.Route, page.Route)}" : "Page (not loaded)");

                case TrakHoundServiceSecurity.ResourcePrefix:
                    if (parts.Length < 2 || parts[1] == "*") return Create(SecurityResourceScope.Service, "All Services", "Every operation of every Service");
                    var service = _services.FirstOrDefault(o => o.Id == parts[1]);
                    var serviceName = service != null ? GetServiceName(service) : parts[1];
                    if (parts.Length < 3 || parts[2] == "*") return Create(SecurityResourceScope.Service, serviceName, "All Operations");
                    if (parts.Length < 4 || parts[3] == "*") return Create(SecurityResourceScope.Service, serviceName, $"All {GetFamilyTitle(parts[2])} Operations");
                    return Create(SecurityResourceScope.Service, serviceName, $"{GetFamilyTitle(parts[2])} · {parts[3]}");

                case TrakHoundSystemSecurity.ResourcePrefix:
                    if (parts.Length < 2 || parts[1] == "*") return Create(SecurityResourceScope.System, "All System API", "Every System API area (Services are authorized per Service)");
                    var systemResource = TrakHoundSystemSecurity.GetResource(pattern);
                    if (systemResource != null) return Create(SecurityResourceScope.System, $"System API · {systemResource.Title}", systemResource.Routes);
                    break;

                case TrakHoundSystemSecurity.LegacyApiInformation:
                    return Describe(TrakHoundSystemSecurity.ApiInformation);
            }

            return Create(SecurityResourceScope.Custom, "Custom Pattern", null);
        }


        public IEnumerable<SecurityResourceOption> GetTargets(SecurityResourceScope scope)
        {
            switch (scope)
            {
                case SecurityResourceScope.Api: return _apis.Select(o => new SecurityResourceOption { Value = o.Id, Text = $"{o.PackageId}  ({o.Route})" });
                case SecurityResourceScope.App: return _apps.Select(o => new SecurityResourceOption { Value = o.Id, Text = $"{o.Name ?? o.PackageId}  ({o.Route})" });
                case SecurityResourceScope.Service: return _services.Select(o => new SecurityResourceOption { Value = o.Id, Text = GetServiceName(o) });
                case SecurityResourceScope.System: return TrakHoundSystemSecurity.Resources.Select(o => new SecurityResourceOption { Value = o.Id.Substring(TrakHoundSystemSecurity.ResourcePrefix.Length + 1), Text = $"{o.Title}  ({o.Routes})" });
            }

            return Enumerable.Empty<SecurityResourceOption>();
        }

        /// <summary>
        /// The specific items within a Target (Api Endpoints, App Pages, or Service Families). Values are the remainder of the Resource ID
        /// </summary>
        public IEnumerable<SecurityResourceOption> GetItems(SecurityResourceScope scope, string targetId)
        {
            switch (scope)
            {
                case SecurityResourceScope.Api:
                    var api = _apis.FirstOrDefault(o => o.Id == targetId);
                    if (api?.Controllers == null) break;
                    return api.Controllers
                        .Where(o => o.EndPoints != null)
                        .SelectMany(c => c.EndPoints.Select(e => new SecurityResourceOption { Value = $"{c.Id}:{e.Id}", Text = FormatEndpoint(api, c, e) }))
                        .OrderBy(o => o.Text);

                case SecurityResourceScope.App:
                    var app = _apps.FirstOrDefault(o => o.Id == targetId);
                    if (app?.Pages == null) break;
                    return app.Pages.Select(o => new SecurityResourceOption { Value = o.Id, Text = CombineRoute(app.Route, o.Route) }).OrderBy(o => o.Text);

                case SecurityResourceScope.Service:
                    return ServiceFamilies.Select(o => new SecurityResourceOption { Value = o, Text = $"{GetFamilyTitle(o)} Operations" });
            }

            return Enumerable.Empty<SecurityResourceOption>();
        }

        public static IEnumerable<SecurityResourceOption> GetServiceOperations(string family)
        {
            if (family != null && _serviceOperations.TryGetValue(family, out var operations))
            {
                return operations.Select(o => new SecurityResourceOption { Value = o.ToLowerInvariant(), Text = $"{o}  ({TrakHoundServiceSecurity.GetPermission(o)})" });
            }

            return Enumerable.Empty<SecurityResourceOption>();
        }

        /// <summary>
        /// The Permissions declared by registered Resources (ex. from TrakHoundPermission attributes on Api Endpoints)
        /// </summary>
        public static IEnumerable<string> GetDeclaredPermissions(ITrakHoundInstance instance)
        {
            return instance.SecurityManager.GetResources()
                .Where(o => o.Permissions != null)
                .SelectMany(o => o.Permissions)
                .Where(o => !string.IsNullOrEmpty(o) && o != "*")
                .Distinct()
                .OrderBy(o => o);
        }

        public static string GetFamilyTitle(string family)
        {
            switch (family)
            {
                case "blobs": return "Blob";
                case "key-values": return "Key Value";
                case "message-queues": return "Message Queue";
                case "time-series": return "Time Series";
                case "messages": return "Message";
                case "tables": return "Table";
                default: return family;
            }
        }

        public static string GetScopeIcon(SecurityResourceScope scope)
        {
            switch (scope)
            {
                case SecurityResourceScope.Everything: return "fa-solid fa-sharp fa-globe";
                case SecurityResourceScope.Api: return "fa-solid fa-sharp fa-circle-location-arrow";
                case SecurityResourceScope.App: return "fa-solid fa-sharp fa-browsers";
                case SecurityResourceScope.Service: return "fa-solid fa-sharp fa-right-left-large";
                case SecurityResourceScope.System: return "fa-solid fa-sharp fa-gear";
                default: return "fa-solid fa-sharp fa-asterisk";
            }
        }


        private static SecurityResourceDescription Create(SecurityResourceScope scope, string title, string subtitle)
        {
            return new SecurityResourceDescription { Scope = scope, Icon = GetScopeIcon(scope), Title = title, Subtitle = subtitle };
        }

        private static string FindEndpoint(TrakHoundApiInformation api, string controllerId, string endpointId)
        {
            var controller = api?.Controllers?.FirstOrDefault(o => o.Id == controllerId);
            if (controller == null) return null;
            if (endpointId == null || endpointId == "*") return $"Controller · {CombineRoute(api.Route, controller.Route)}";

            var endpoint = controller.EndPoints?.FirstOrDefault(o => o.Id == endpointId);
            return endpoint != null ? FormatEndpoint(api, controller, endpoint) : null;
        }

        private static string FormatEndpoint(TrakHoundApiInformation api, TrakHoundApiControllerInformation controller, TrakHoundApiEndpointInformation endpoint)
        {
            var method = endpoint.Method?.ToString().ToUpperInvariant() ?? endpoint.Type.ToString().ToUpperInvariant();
            return $"{method} {CombineRoute(api.Route, controller.Route, endpoint.Route)}";
        }

        private static string CombineRoute(params string[] routes) => "/" + Url.Combine(routes);

        private static string GetServiceName(TrakHoundServiceConfiguration configuration)
        {
            return !string.IsNullOrEmpty(configuration.Name) ? configuration.Name : $"{configuration.PackageId} ({configuration.Id})";
        }
    }
}
