// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Instances;

namespace TrakHound.Instance.Pages.Settings
{
    /// <summary>
    /// Passed to the Settings sections so that the page can update its Save state when a value changes
    /// </summary>
    public class SettingsEditContext
    {
        private readonly Action _changed;

        public SettingsEditContext(Action changed)
        {
            _changed = changed;
        }

        public void Changed() => _changed?.Invoke();
    }


    /// <summary>
    /// The editable Instance Configuration values (a copy, so changes are only applied when Saved)
    /// </summary>
    public class InstanceSettingsModel
    {
        /// <summary>
        /// The settings that can be a Variable reference on the Settings page (their YAML paths), ex. a port that is different on each device
        /// </summary>
        public static readonly IReadOnlyList<string> ReferencePaths = new[] { "name", "description", "httpAddress", "httpPort", "basePath", "maxRequestBodySizeMb", "adminHttpPort", "adminHttpAddress" };

        /// <summary>
        /// The settings that are Variable references (YAML path => reference, ex. "httpPort" => "${port}")
        /// </summary>
        public Dictionary<string, string> References { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Name { get; set; }

        public string Description { get; set; }

        public string BasePath { get; set; }

        public string HttpAddress { get; set; }

        public int HttpPort { get; set; }

        public int MaxRequestBodySizeMb { get; set; }

        public bool AdminPortEnabled { get; set; }

        /// <summary>
        /// Kept when the Admin Port is Disabled so it is restored when Enabled again
        /// </summary>
        public int AdminHttpPort { get; set; }

        public string AdminHttpAddress { get; set; }

        /// <summary>
        /// Trusted Proxy entries (one per line)
        /// </summary>
        public string TrustedProxies { get; set; }

        /// <summary>
        /// Admin Interface Allowed Address entries (one per line)
        /// </summary>
        public string AdminAllowedAddresses { get; set; }

        public bool SystemApiEnabled { get; set; }

        public bool SystemApiUnprotectedRemoteAccess { get; set; }

        public bool McpEnabled { get; set; }

        public bool ApiHttpEnabled { get; set; }

        public bool AppsHttpEnabled { get; set; }

        public bool InitializationPageEnabled { get; set; }

        public string InitializationPagePath { get; set; }

        public bool ErrorPageEnabled { get; set; }

        public bool PackageUpdateCheckEnabled { get; set; }

        /// <summary>
        /// Hours (kept when Checks are Disabled so it is restored when Enabled again)
        /// </summary>
        public int PackageUpdateCheckInterval { get; set; }

        public bool PackageInventoryReportEnabled { get; set; }

        /// <summary>
        /// Hours (kept when Heartbeats are Disabled so it is restored when Enabled again)
        /// </summary>
        public int PackageInventoryReportInterval { get; set; }

        public TrakHound.Packages.TrakHoundPackageVerificationMode PackageVerification { get; set; }

        public bool MetricsEnabled { get; set; }

        public bool MetricsApi { get; set; }

        public bool MetricsServices { get; set; }

        public bool MetricsFunctions { get; set; }

        public bool MetricsWorkers { get; set; }

        public bool MetricsApps { get; set; }

        public bool MetricsHttp { get; set; }

        public bool MetricsStreams { get; set; }

        public bool MetricsMcp { get; set; }

        public bool MetricsCpu { get; set; }

        /// <summary>
        /// The most recent HTTP Requests to keep (0 keeps none)
        /// </summary>
        public int HttpRequestHistory { get; set; }

        /// <summary>
        /// The most recent API Requests to keep (0 keeps none)
        /// </summary>
        public int ApiRequestHistory { get; set; }

        /// <summary>
        /// The most recently closed API Streams to keep (0 keeps none)
        /// </summary>
        public int ApiStreamHistory { get; set; }

        /// <summary>
        /// Health Critical entries (one per line)
        /// </summary>
        public string HealthCritical { get; set; }

        /// <summary>
        /// Health Ignore entries (one per line)
        /// </summary>
        public string HealthIgnore { get; set; }

        /// <summary>
        /// A copy of the Rate Limits (applied when Saved)
        /// </summary>
        public TrakHoundInstanceRateLimitConfiguration RateLimits { get; set; }


        public static InstanceSettingsModel Create(TrakHoundInstanceConfiguration configuration)
        {
            var metrics = configuration.Metrics ?? new TrakHoundInstanceMetricsConfiguration();
            var health = configuration.Health ?? new TrakHoundInstanceHealthConfiguration();
            var rateLimits = configuration.RateLimits?.Clone() ?? new TrakHoundInstanceRateLimitConfiguration();
            rateLimits.Api ??= new TrakHoundRateLimit();
            rateLimits.SystemApi ??= new TrakHoundRateLimit();
            rateLimits.McpTools ??= new TrakHoundRateLimit();

            return new InstanceSettingsModel
            {
                Name = configuration.Name,
                Description = configuration.Description,
                BasePath = configuration.BasePath,
                HttpAddress = configuration.HttpAddress,
                HttpPort = configuration.HttpPort,
                MaxRequestBodySizeMb = configuration.MaxRequestBodySizeMb,
                AdminPortEnabled = configuration.AdminHttpPort > 0,
                AdminHttpPort = configuration.AdminHttpPort > 0 ? configuration.AdminHttpPort : configuration.HttpPort + 1,
                AdminHttpAddress = configuration.AdminHttpAddress,
                TrustedProxies = JoinLines(configuration.TrustedProxies),
                AdminAllowedAddresses = JoinLines(configuration.AdminAllowedAddresses),
                SystemApiEnabled = configuration.SystemApiEnabled,
                SystemApiUnprotectedRemoteAccess = configuration.SystemApiUnprotectedRemoteAccess,
                McpEnabled = configuration.McpEnabled,
                ApiHttpEnabled = configuration.ApiHttpEnabled,
                AppsHttpEnabled = configuration.AppsHttpEnabled,
                InitializationPageEnabled = configuration.InitializationPageEnabled,
                InitializationPagePath = configuration.InitializationPagePath,
                ErrorPageEnabled = configuration.ErrorPageEnabled,
                PackageUpdateCheckEnabled = configuration.PackageUpdateCheckInterval > 0,
                PackageUpdateCheckInterval = configuration.PackageUpdateCheckInterval > 0 ? configuration.PackageUpdateCheckInterval : TrakHoundInstanceConfiguration.DefaultPackageUpdateCheckInterval,
                PackageInventoryReportEnabled = configuration.PackageInventoryReportInterval > 0,
                PackageInventoryReportInterval = configuration.PackageInventoryReportInterval > 0 ? configuration.PackageInventoryReportInterval : TrakHoundInstanceConfiguration.DefaultPackageInventoryReportInterval,
                PackageVerification = configuration.PackageVerification,
                MetricsEnabled = metrics.Enabled,
                MetricsApi = metrics.Api,
                MetricsServices = metrics.Services,
                MetricsFunctions = metrics.Functions,
                MetricsWorkers = metrics.Workers,
                MetricsApps = metrics.Apps,
                MetricsHttp = metrics.Http,
                MetricsStreams = metrics.Streams,
                MetricsMcp = metrics.Mcp,
                MetricsCpu = metrics.Cpu,
                HttpRequestHistory = metrics.HttpRequestHistory,
                ApiRequestHistory = metrics.ApiRequestHistory,
                ApiStreamHistory = metrics.ApiStreamHistory,
                HealthCritical = JoinLines(health.Critical),
                HealthIgnore = JoinLines(health.Ignore),
                RateLimits = rateLimits,
                References = configuration.References.Where(o => ReferencePaths.Contains(o.Key)).ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal)
            };
        }

        /// <summary>
        /// The references that can't be saved (each must be one reference, ex. "${port}")
        /// </summary>
        public List<string> GetReferenceErrors()
        {
            return References.Where(o => !TrakHoundConfigurationVariables.IsReference(o.Value?.Trim()))
                .Select(o => $"'{o.Value}' ({o.Key}) isn't a Variable reference. Enter one, ex. ${{port}}")
                .ToList();
        }

        public void ApplyTo(TrakHoundInstanceConfiguration configuration)
        {
            configuration.Name = Normalize(Name);
            configuration.Description = Normalize(Description);
            configuration.BasePath = Normalize(BasePath);
            configuration.HttpAddress = Normalize(HttpAddress);
            configuration.HttpPort = HttpPort;
            configuration.MaxRequestBodySizeMb = Math.Max(0, MaxRequestBodySizeMb); // Applied to the next Request
            configuration.AdminHttpPort = AdminPortEnabled ? AdminHttpPort : 0;
            configuration.AdminHttpAddress = AdminPortEnabled ? Normalize(AdminHttpAddress) : null;
            var trustedProxies = SplitLines(TrustedProxies);
            configuration.TrustedProxies = trustedProxies.Count > 0 ? trustedProxies : null; // Applied to the next Request
            var adminAllowedAddresses = SplitLines(AdminAllowedAddresses);
            configuration.AdminAllowedAddresses = adminAllowedAddresses.Count > 0 ? adminAllowedAddresses : null; // Applied to the next Request
            configuration.SystemApiEnabled = SystemApiEnabled;
            configuration.SystemApiUnprotectedRemoteAccess = SystemApiUnprotectedRemoteAccess;
            configuration.McpEnabled = McpEnabled;
            configuration.ApiHttpEnabled = ApiHttpEnabled;
            configuration.AppsHttpEnabled = AppsHttpEnabled;
            configuration.InitializationPageEnabled = InitializationPageEnabled;
            configuration.InitializationPagePath = Normalize(InitializationPagePath);
            configuration.ErrorPageEnabled = ErrorPageEnabled;
            configuration.PackageUpdateCheckInterval = PackageUpdateCheckEnabled ? Math.Max(1, PackageUpdateCheckInterval) : 0;
            configuration.PackageInventoryReportInterval = PackageInventoryReportEnabled ? Math.Max(1, PackageInventoryReportInterval) : 0;
            configuration.PackageVerification = PackageVerification; // Applied when the Instance is restarted

            // Applied when the Instance is restarted
            configuration.Metrics ??= new TrakHoundInstanceMetricsConfiguration();
            configuration.Metrics.Enabled = MetricsEnabled;
            configuration.Metrics.Api = MetricsApi;
            configuration.Metrics.Services = MetricsServices;
            configuration.Metrics.Functions = MetricsFunctions;
            configuration.Metrics.Workers = MetricsWorkers;
            configuration.Metrics.Apps = MetricsApps;
            configuration.Metrics.Http = MetricsHttp;
            configuration.Metrics.Streams = MetricsStreams;
            configuration.Metrics.Mcp = MetricsMcp;
            configuration.Metrics.Cpu = MetricsCpu;
            configuration.Metrics.HttpRequestHistory = Math.Clamp(HttpRequestHistory, 0, TrakHound.Http.TrakHoundHttpRequestHistory.MaxCapacity);
            configuration.Metrics.ApiRequestHistory = Math.Clamp(ApiRequestHistory, 0, TrakHound.Instance.Api.TrakHoundApiConnectionManager.MaxRequestHistory);
            configuration.Metrics.ApiStreamHistory = Math.Clamp(ApiStreamHistory, 0, TrakHound.Instance.Api.TrakHoundApiStreamManager.MaxClosedHistory);

            // Applied on the next Health evaluation
            configuration.Health ??= new TrakHoundInstanceHealthConfiguration();
            configuration.Health.Critical = SplitLines(HealthCritical);
            configuration.Health.Ignore = SplitLines(HealthIgnore);

            // Applied to the next Request (a copy, so later edits aren't applied until Saved)
            var rateLimits = RateLimits?.Clone() ?? new TrakHoundInstanceRateLimitConfiguration();
            foreach (var limit in new[] { rateLimits.Api, rateLimits.SystemApi, rateLimits.McpTools })
            {
                if (limit == null) continue;
                limit.PermitLimit = Math.Max(0, limit.PermitLimit);
                limit.Window = Math.Clamp(limit.Window, 1, TrakHoundRateLimit.MaxWindow);
                limit.ConcurrencyLimit = Math.Max(0, limit.ConcurrencyLimit);
            }
            configuration.RateLimits = rateLimits;

            // References are written while their settings keep the values just applied (the Admin Port's are dropped when it is Disabled)
            foreach (var path in ReferencePaths)
            {
                var reference = References.GetValueOrDefault(path)?.Trim();
                var adminPort = path == "adminHttpPort" || path == "adminHttpAddress";
                if (TrakHoundConfigurationVariables.IsReference(reference) && (!adminPort || AdminPortEnabled)) configuration.SetReference(path, reference);
                else configuration.RemoveReference(path);
            }
        }

        /// <summary>
        /// A value that changes when any setting changes (used to detect unsaved changes)
        /// </summary>
        public string GetSnapshot()
        {
            return string.Join('\u001f', Normalize(Name), Normalize(Description), Normalize(BasePath), Normalize(HttpAddress), HttpPort, MaxRequestBodySizeMb, AdminPortEnabled ? AdminHttpPort : 0, AdminPortEnabled ? Normalize(AdminHttpAddress) : null, JoinLines(SplitLines(TrustedProxies)), JoinLines(SplitLines(AdminAllowedAddresses)), SystemApiEnabled, SystemApiUnprotectedRemoteAccess, McpEnabled, ApiHttpEnabled, AppsHttpEnabled, InitializationPageEnabled, Normalize(InitializationPagePath), ErrorPageEnabled, PackageUpdateCheckEnabled ? PackageUpdateCheckInterval : 0, PackageInventoryReportEnabled ? PackageInventoryReportInterval : 0,
                MetricsEnabled, MetricsApi, MetricsServices, MetricsFunctions, MetricsWorkers, MetricsApps, MetricsHttp, MetricsStreams, MetricsMcp, MetricsCpu, HttpRequestHistory, ApiRequestHistory, ApiStreamHistory,
                JoinLines(SplitLines(HealthCritical)), JoinLines(SplitLines(HealthIgnore)),
                RateLimits?.Enabled, GetSnapshot(RateLimits?.Api), GetSnapshot(RateLimits?.SystemApi), GetSnapshot(RateLimits?.McpTools),
                string.Join(';', References.OrderBy(o => o.Key).Select(o => $"{o.Key}={o.Value}")));
        }

        private static string GetSnapshot(TrakHoundRateLimit limit) => limit != null ? $"{limit.PermitLimit}/{limit.Window}/{limit.ConcurrencyLimit}" : null;

        /// <summary>
        /// Whether the Metrics settings are different from the ones the Instance is running with
        /// </summary>
        public bool MetricsDifferFrom(TrakHoundInstanceMetricsConfiguration running)
        {
            running ??= new TrakHoundInstanceMetricsConfiguration();
            return MetricsEnabled != running.Enabled || MetricsApi != running.Api || MetricsServices != running.Services ||
                MetricsFunctions != running.Functions || MetricsWorkers != running.Workers || MetricsApps != running.Apps ||
                MetricsHttp != running.Http || MetricsStreams != running.Streams || MetricsMcp != running.Mcp || MetricsCpu != running.Cpu ||
                HttpRequestHistory != running.HttpRequestHistory || ApiRequestHistory != running.ApiRequestHistory || ApiStreamHistory != running.ApiStreamHistory;
        }

        private static string JoinLines(IEnumerable<string> values) => values != null ? string.Join('\n', values) : null;

        // One entry per line (trimmed, blank lines and duplicates removed)
        private static List<string> SplitLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            return value.Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Prevent Empty strings being saved
        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
