// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to access the Apis, Apps, Functions, Workers, Services, and other features of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundClient 
    {
        /// <summary>
        /// The ID of the Service Router used for Service Requests (null or "default" uses the Instance's default Router)
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// Client used to send Requests to the Apis hosted by the Instance
        /// </summary>
        ITrakHoundApiClient Api { get; }

        /// <summary>
        /// Client used to query and manage the Apps hosted by the Instance
        /// </summary>
        ITrakHoundAppsClient Apps { get; }

        /// <summary>
        /// Client used to query and Run the Functions hosted by the Instance
        /// </summary>
        ITrakHoundFunctionsClient Functions { get; }

        /// <summary>
        /// Client used to query and control the Workers hosted by the Instance
        /// </summary>
        ITrakHoundWorkersClient Workers { get; }

        /// <summary>
        /// Client used to query the Operation Buffers of the Instance
        /// </summary>
        ITrakHoundBuffersClient Buffers { get; }

        /// <summary>
        /// Client used to query the Modules loaded by the Instance
        /// </summary>
        ITrakHoundModulesClient Modules { get; }

        /// <summary>
        /// Client used to query the Stream connection Metrics (WebSockets and Server-Sent Events) of the Instance
        /// </summary>
        ITrakHoundStreamsClient Streams { get; }

        /// <summary>
        /// Client used to query the MCP Server (Model Context Protocol) Metrics of the Instance
        /// </summary>
        ITrakHoundMcpClient Mcp { get; }

        /// <summary>
        /// Client used to query the HTTP Request Metrics of the Instance
        /// </summary>
        ITrakHoundHttpRequestsClient HttpRequests { get; }

        /// <summary>
        /// Client used to access the Services (Blobs, KeyValues, Messages, Message Queues, Tables, Time Series), Drivers, and Routers of the Instance
        /// </summary>
        ITrakHoundServicesClient Services { get; }

        /// <summary>
        /// Client used to access the files in the Volumes of the Instance
        /// </summary>
        ITrakHoundVolumesClient Volumes { get; }

        /// <summary>
        /// Client used to list, query, and subscribe to the Logs of the Instance
        /// </summary>
        ITrakHoundLogsClient Logs { get; }

        /// <summary>
        /// Client used to query and control the Instance itself
        /// </summary>
        ITrakHoundInstancesClient Instances { get; }


        /// <summary>
        /// Gets the absolute URL for the specified route on the Instance
        /// </summary>
        string GetLink(string route);
    }
}
