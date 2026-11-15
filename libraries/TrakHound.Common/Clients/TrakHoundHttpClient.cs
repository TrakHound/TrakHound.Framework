// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// ITrakHoundClient that connects to a remote TrakHound Instance using HTTP and WebSockets
    /// </summary>
    public class TrakHoundHttpClient : ITrakHoundClient
    {
        private readonly string _httpBaseUrl;
        private readonly string _webSocketsBaseUrl;
        private readonly string _routerId;
        private readonly TrakHoundHttpClientConfiguration _clientConfiguration;
        private readonly TrakHoundHttpApiClient _api;
        private readonly TrakHoundHttpAppsClient _apps;
        private readonly TrakHoundHttpWorkersClient _workers;
        private readonly TrakHoundHttpBuffersClient _buffers;
        private readonly TrakHoundHttpModulesClient _modules;
        private readonly TrakHoundHttpStreamsClient _streams;
        private readonly TrakHoundHttpMcpClient _mcp;
        private readonly TrakHoundHttpRequestsClient _httpRequests;
        private readonly TrakHoundHttpFunctionsClient _functions;
        private readonly TrakHoundHttpServicesClient _services;
        private readonly TrakHoundHttpVolumesClient _volumes;
        private readonly TrakHoundHttpLogsClient _logs;
        private readonly TrakHoundHttpInstancesClient _instances;
        private readonly TrakHoundHttpRequestSender _requests;


        /// <summary>
        /// The Configuration (Hostname, Port, Path, SSL) used to connect to the Instance
        /// </summary>
        public TrakHoundHttpClientConfiguration ClientConfiguration => _clientConfiguration;

        /// <summary>
        /// Sends HTTP Requests with the Authentication Headers of the Client Configuration (ex. an API Key or Module Token)
        /// </summary>
        public TrakHoundHttpRequestSender Requests => _requests;

        /// <summary>
        /// The base URL used for HTTP Requests (ex. http://localhost:8472/)
        /// </summary>
        public string HttpBaseUrl => _httpBaseUrl;

        /// <summary>
        /// The base URL used for WebSocket connections (ex. ws://localhost:8472/)
        /// </summary>
        public string WebSocketsBaseUrl => _webSocketsBaseUrl;

        /// <inheritdoc/>
        public string RouterId => _routerId;

        /// <inheritdoc/>
        public ITrakHoundApiClient Api => _api;

        /// <inheritdoc/>
        public ITrakHoundAppsClient Apps => _apps;

        /// <inheritdoc/>
        public ITrakHoundFunctionsClient Functions => _functions;

        /// <inheritdoc/>
        public ITrakHoundWorkersClient Workers => _workers;

        /// <inheritdoc/>
        public ITrakHoundBuffersClient Buffers => _buffers;

        /// <inheritdoc/>
        public ITrakHoundModulesClient Modules => _modules;

        /// <inheritdoc/>
        public ITrakHoundStreamsClient Streams => _streams;

        /// <inheritdoc/>
        public ITrakHoundMcpClient Mcp => _mcp;

        /// <inheritdoc/>
        public ITrakHoundHttpRequestsClient HttpRequests => _httpRequests;

        /// <inheritdoc/>
        public ITrakHoundServicesClient Services => _services;

        /// <inheritdoc/>
        public ITrakHoundVolumesClient Volumes => _volumes;

        /// <inheritdoc/>
        public ITrakHoundLogsClient Logs => _logs;

        /// <inheritdoc/>
        public ITrakHoundInstancesClient Instances => _instances;


        /// <summary>
        /// Creates a new Client that connects to the Instance at the specified hostname, port, and path
        /// </summary>
        public TrakHoundHttpClient(
            string hostname,
            int port = TrakHoundHttpClientConfiguration.DefaultPort,
            string path = TrakHoundHttpClientConfiguration.DefaultPath,
            bool useSSL = TrakHoundHttpClientConfiguration.DefaultUseSSL,
            string routerId = null
            )
        {
            var clientConfiguration = new TrakHoundHttpClientConfiguration(hostname, port, path, useSSL);
            _clientConfiguration = clientConfiguration;

            _httpBaseUrl = clientConfiguration.GetHttpBaseUrl();
            _webSocketsBaseUrl = clientConfiguration.GetWebSocketBaseUrl();
            _routerId = routerId;
            _requests = new TrakHoundHttpRequestSender(clientConfiguration);

            _api = new TrakHoundHttpApiClient(this);
            _apps = new TrakHoundHttpAppsClient(this);
            _functions = new TrakHoundHttpFunctionsClient(this);
            _workers = new TrakHoundHttpWorkersClient(this);
            _buffers = new TrakHoundHttpBuffersClient(this);
            _modules = new TrakHoundHttpModulesClient(this);
            _streams = new TrakHoundHttpStreamsClient(this);
            _mcp = new TrakHoundHttpMcpClient(this);
            _httpRequests = new TrakHoundHttpRequestsClient(this);
            _services = new TrakHoundHttpServicesClient(this);
            _volumes = new TrakHoundHttpVolumesClient(this);
            _logs = new TrakHoundHttpLogsClient(this);
            _instances = new TrakHoundHttpInstancesClient(this);
        }

        /// <summary>
        /// Creates a new Client that connects to the Instance using the specified Client Configuration
        /// </summary>
        public TrakHoundHttpClient(TrakHoundHttpClientConfiguration clientConfiguration, string routerId = null)
        {
            // Set Client Configuration
            _clientConfiguration = clientConfiguration;
            if (clientConfiguration != null)
            {
                _httpBaseUrl = clientConfiguration.GetHttpBaseUrl();
                _webSocketsBaseUrl = clientConfiguration.GetWebSocketBaseUrl();

                _routerId = routerId;
            }

            _requests = new TrakHoundHttpRequestSender(clientConfiguration);

            _api = new TrakHoundHttpApiClient(this);
            _apps = new TrakHoundHttpAppsClient(this);
            _functions = new TrakHoundHttpFunctionsClient(this);
            _workers = new TrakHoundHttpWorkersClient(this);
            _buffers = new TrakHoundHttpBuffersClient(this);
            _modules = new TrakHoundHttpModulesClient(this);
            _streams = new TrakHoundHttpStreamsClient(this);
            _mcp = new TrakHoundHttpMcpClient(this);
            _httpRequests = new TrakHoundHttpRequestsClient(this);
            _services = new TrakHoundHttpServicesClient(this);
            _volumes = new TrakHoundHttpVolumesClient(this);
            _logs = new TrakHoundHttpLogsClient(this);
            _instances = new TrakHoundHttpInstancesClient(this);
        }


        /// <summary>
        /// Creates a Client from the Environment Variables that the Instance gives a Module process ("TRAKHOUND_URL", "TRAKHOUND_MODULE_TOKEN"
        /// and "TRAKHOUND_ROUTER_ID"), so that Requests are made as the Module. Returns null if "TRAKHOUND_URL" is not set
        /// </summary>
        public static TrakHoundHttpClient FromModuleEnvironment()
        {
            var configuration = TrakHoundHttpClientConfiguration.FromModuleEnvironment();
            if (configuration == null) return null;

            var routerId = Environment.GetEnvironmentVariable(TrakHoundHttpConstants.RouterIdVariable);
            return new TrakHoundHttpClient(configuration, !string.IsNullOrEmpty(routerId) ? routerId : null);
        }


        internal string GetRouterId(string routerId)
        {
            string x = null;
            if (!string.IsNullOrEmpty(routerId)) x = routerId;
            else x = RouterId;
            return x != "default" ? x : null;
        }

        /// <inheritdoc/>
        public string GetLink(string route)
        {
            return Url.Combine(_httpBaseUrl, route);
        }
    }
}
