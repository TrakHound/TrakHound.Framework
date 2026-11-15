// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Configurations;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// Configuration used to connect a TrakHoundHttpClient to a TrakHound Instance
    /// </summary>
    public class TrakHoundHttpClientConfiguration
    {
        /// <summary>
        /// The default Hostname (localhost)
        /// </summary>
        public const string DefaultHostname = "localhost";
        /// <summary>
        /// The default Port (8472)
        /// </summary>
        public const int DefaultPort = 8472;
        /// <summary>
        /// The default base Path (/)
        /// </summary>
        public const string DefaultPath = "/";
        /// <summary>
        /// The default for whether SSL (https/wss) is used (false)
        /// </summary>
        public const bool DefaultUseSSL = false;
        /// <summary>
        /// The default Connection Timeout in milliseconds (60000)
        /// </summary>
        public const int DefaultConnectionTimeout = 60000;

        /// <summary>
        /// The Hostname or IP Address of the Instance
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// The HTTP Port of the Instance
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// The base Path that the Instance is served at
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets whether SSL is used (https and wss instead of http and ws)
        /// </summary>
        public bool UseSSL { get; set; }

        /// <summary>
        /// The Connection Timeout in milliseconds
        /// </summary>
        public int ConnectionTimeout { get; set; }

        /// <summary>
        /// An API Key issued by the Instance, sent in the "X-API-Key" Header of each Request
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// A Bearer Token (ex. from a Token Identity Provider), sent in the "Authorization" Header of each Request
        /// </summary>
        public string BearerToken { get; set; }

        /// <summary>
        /// A Module Token issued by the Instance to a Module (ex. an Api that runs in its own process), sent in the "X-TrakHound-Module-Token" Header
        /// of each Request. Requests are then made as the Module, so the Module's Zone applies to them
        /// </summary>
        public string ModuleToken { get; set; }

        /// <summary>
        /// Additional Headers sent with each Request (these take precedence over the Authentication Headers)
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }


        /// <summary>
        /// Creates a new Client Configuration (the Connection Timeout is set to the default)
        /// </summary>
        public TrakHoundHttpClientConfiguration(string hostname = DefaultHostname, int port = DefaultPort, string path = DefaultPath, bool useSSL = DefaultUseSSL)
        {
            Hostname = hostname;
            Port = port;
            Path = path;
            UseSSL = useSSL;
            ConnectionTimeout = DefaultConnectionTimeout;
        }


        /// <summary>
        /// Creates a Client Configuration from the URL of an Instance (ex. http://localhost:8472/ or https://example.com/trakhound/). Returns null if the URL is not valid
        /// </summary>
        public static TrakHoundHttpClientConfiguration FromUrl(string url)
        {
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var useSSL = uri.Scheme == Uri.UriSchemeHttps;
                var path = !string.IsNullOrEmpty(uri.AbsolutePath) ? uri.AbsolutePath : DefaultPath;
                return new TrakHoundHttpClientConfiguration(uri.Host, uri.Port, path, useSSL);
            }

            return null;
        }

        /// <summary>
        /// Creates a Client Configuration from the Environment Variables that the Instance gives a Module process
        /// ("TRAKHOUND_URL" and "TRAKHOUND_MODULE_TOKEN"). Returns null if "TRAKHOUND_URL" is not set
        /// </summary>
        public static TrakHoundHttpClientConfiguration FromModuleEnvironment()
        {
            var configuration = FromUrl(Environment.GetEnvironmentVariable(TrakHoundHttpConstants.InstanceUrlVariable));
            if (configuration != null)
            {
                var moduleToken = Environment.GetEnvironmentVariable(TrakHoundHttpConstants.ModuleTokenVariable);
                if (!string.IsNullOrEmpty(moduleToken)) configuration.ModuleToken = moduleToken;
            }

            return configuration;
        }


        /// <summary>
        /// Gets the Headers sent with each Request (the Authentication Headers and any additional Headers). Null if there are none
        /// </summary>
        public IDictionary<string, string> GetHeaders()
        {
            Dictionary<string, string> headers = null;

            if (!string.IsNullOrEmpty(ApiKey)) (headers ??= CreateHeaders())[TrakHoundHttpConstants.ApiKeyHeader] = ApiKey;
            if (!string.IsNullOrEmpty(BearerToken)) (headers ??= CreateHeaders())["Authorization"] = $"Bearer {BearerToken}";
            if (!string.IsNullOrEmpty(ModuleToken)) (headers ??= CreateHeaders())[TrakHoundHttpConstants.ModuleTokenHeader] = ModuleToken;

            if (!Headers.IsNullOrEmpty())
            {
                headers ??= CreateHeaders();
                foreach (var header in Headers)
                {
                    if (!string.IsNullOrEmpty(header.Key)) headers[header.Key] = header.Value;
                }
            }

            return headers;
        }

        private static Dictionary<string, string> CreateHeaders() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        /// <summary>
        /// Gets the base URL used for HTTP Requests (ex. http://localhost:8472/)
        /// </summary>
        public string GetHttpBaseUrl()
        {
            var protocol = UseSSL ? "https" : "http";

            return Url.Combine($"{protocol}://{Hostname}:{Port}", Path);
        }

        /// <summary>
        /// Gets the base URL used for WebSocket connections (ex. ws://localhost:8472/)
        /// </summary>
        public string GetWebSocketBaseUrl()
        {
            var protocol = UseSSL ? "wss" : "ws";

            return Url.Combine($"{protocol}://{Hostname}:{Port}", Path);
        }
    }

    /// <summary>
    /// Extension methods for creating a TrakHoundHttpClientConfiguration
    /// </summary>
    public static class TrakHoundClientConfigurationExtensions
    {
        /// <summary>
        /// Creates a Client Configuration from the Instance settings of the Environment Configuration (defaults if the Instance is not set)
        /// </summary>
        public static TrakHoundHttpClientConfiguration GetClientConfiguration(this TrakHoundEnvironmentConfiguration environmentConfiguration)
        {
            if (environmentConfiguration != null && environmentConfiguration.Instance != null)
            {
                return new TrakHoundHttpClientConfiguration(
                    environmentConfiguration.Instance.Hostname,
                    environmentConfiguration.Instance.Port,
                    environmentConfiguration.Instance.Path,
                    environmentConfiguration.Instance.UseSSL
                    );
            }
            else
            {
                return new TrakHoundHttpClientConfiguration();
            }
        }
    }
}
