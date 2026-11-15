// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Testing.Clients;

namespace TrakHound.Testing
{
    /// <summary>
    /// An ITrakHoundClient for tests, without an Instance. Services (Key Values and Blobs in memory, or a Service you Use) and Functions
    /// (handlers you Register) work; every other part throws a NotSupportedException until you set it to your own implementation (ex. a mock)
    /// </summary>
    public class TrakHoundTestClient : ITrakHoundClient
    {
        public string RouterId { get; set; }

        /// <summary>
        /// Client.Services (KeyValues, Blobs and TimeSeries in memory by default)
        /// </summary>
        public TrakHoundTestServicesClient Services { get; } = new TrakHoundTestServicesClient();

        /// <summary>
        /// Client.Functions (Run calls the registered handlers)
        /// </summary>
        public TrakHoundTestFunctionsClient Functions { get; } = new TrakHoundTestFunctionsClient();

        public ITrakHoundApiClient Api { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundApiClient>(nameof(Api));

        public ITrakHoundAppsClient Apps { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundAppsClient>(nameof(Apps));

        public ITrakHoundWorkersClient Workers { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundWorkersClient>(nameof(Workers));

        public ITrakHoundBuffersClient Buffers { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundBuffersClient>(nameof(Buffers));

        public ITrakHoundModulesClient Modules { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundModulesClient>(nameof(Modules));

        public ITrakHoundStreamsClient Streams { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundStreamsClient>(nameof(Streams));

        public ITrakHoundMcpClient Mcp { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundMcpClient>(nameof(Mcp));

        public ITrakHoundHttpRequestsClient HttpRequests { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundHttpRequestsClient>(nameof(HttpRequests));

        public ITrakHoundVolumesClient Volumes { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundVolumesClient>(nameof(Volumes));

        public ITrakHoundLogsClient Logs { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundLogsClient>(nameof(Logs));

        public ITrakHoundInstancesClient Instances { get; set; } = TrakHoundNotSupportedProxy.Create<ITrakHoundInstancesClient>(nameof(Instances));

        ITrakHoundServicesClient ITrakHoundClient.Services => Services;

        ITrakHoundFunctionsClient ITrakHoundClient.Functions => Functions;


        public string GetLink(string route) => route;
    }
}
