// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Http
{
    /// <summary>
    /// Header names and Route prefixes used by the TrakHound HTTP (System) API
    /// </summary>
    public static class TrakHoundHttpConstants
    {
        /// <summary>
        /// The prefix used for TrakHound HTTP Headers
        /// </summary>
        public const string HeaderPrefix = "trakhound";
        /// <summary>
        /// The prefix of the Headers used to return Api Parameters (followed by "-" and the kebab-case parameter name)
        /// </summary>
        public const string ApiParameterHeaderPrefix = "trakhound-parameter";
        /// <summary>
        /// The Header that contains the Id of the Router that handled a Service Request
        /// </summary>
        public const string RouterIdHeader = "TrakHound-Router-Id";
        /// <summary>
        /// The Header that contains the Id of the Service that handled a Service Request
        /// </summary>
        public const string ServiceIdHeader = "TrakHound-Service-Id";
        /// <summary>
        /// The Header that contains an API Key issued by the Instance (an API Key can also be sent as a Bearer Token)
        /// </summary>
        public const string ApiKeyHeader = "X-API-Key";
        /// <summary>
        /// The Header that contains a Module Token, which identifies the Module (ex. an Api) making the Request so that its Zone applies
        /// (a Module Token can also be sent as a Bearer Token)
        /// </summary>
        public const string ModuleTokenHeader = "X-TrakHound-Module-Token";
        /// <summary>
        /// The prefix of every Module Token
        /// </summary>
        public const string ModuleTokenPrefix = "thm_";

        /// <summary>
        /// The Environment Variable that a Module process is given the URL of the Instance in (ex. http://localhost:8472/)
        /// </summary>
        public const string InstanceUrlVariable = "TRAKHOUND_URL";
        /// <summary>
        /// The Environment Variable that a Module process is given its Module Token in
        /// </summary>
        public const string ModuleTokenVariable = "TRAKHOUND_MODULE_TOKEN";
        /// <summary>
        /// The Environment Variable that a Module process is given the ID of its Service Router in
        /// </summary>
        public const string RouterIdVariable = "TRAKHOUND_ROUTER_ID";

        /// <summary>
        /// Route prefix for the Instances endpoints
        /// </summary>
        public const string InstancesPrefix = "_instances";
        /// <summary>
        /// Route prefix for the Volumes endpoints
        /// </summary>
        public const string VolumesPrefix = "_volumes";
        /// <summary>
        /// Route prefix for the Logs endpoints
        /// </summary>
        public const string LogsPrefix = "_logs";

        /// <summary>
        /// Route prefix for the Apps endpoints
        /// </summary>
        public const string AppsPrefix = "_apps";
        /// <summary>
        /// Route prefix for the Api endpoints
        /// </summary>
        public const string ApiPrefix = "_api";
        /// <summary>
        /// Route prefix for the Functions endpoints
        /// </summary>
        public const string FunctionsPrefix = "_functions";
        /// <summary>
        /// Route prefix for the Workers endpoints
        /// </summary>
        public const string WorkersPrefix = "_workers";
        /// <summary>
        /// Route prefix for the Buffers endpoints
        /// </summary>
        public const string BuffersPrefix = "_buffers";
        /// <summary>
        /// Route prefix for the Modules endpoints
        /// </summary>
        public const string ModulesPrefix = "_modules";
        /// <summary>
        /// Route prefix for the Streams (Metrics) endpoints
        /// </summary>
        public const string StreamsPrefix = "_streams";
        /// <summary>
        /// Route prefix for the MCP Server (Model Context Protocol) and its Metrics
        /// </summary>
        public const string McpPrefix = "_mcp";
        /// <summary>
        /// Route prefix for the HTTP Requests (Metrics) endpoints
        /// </summary>
        public const string HttpPrefix = "_http";

        /// <summary>
        /// Route prefix for the Services endpoints
        /// </summary>
        public const string ServicesPrefix = "_services";
        /// <summary>
        /// Route prefix for the Service Routers endpoints
        /// </summary>
        public const string ServiceRoutersPrefix = "_services/routers";
        /// <summary>
        /// Route prefix for the Service Configurations endpoints
        /// </summary>
        public const string ServiceConfigurationsPrefix = "_services/configurations";
        /// <summary>
        /// Route prefix for the Blob Service endpoints
        /// </summary>
        public const string BlobServicePrefix = "_services/blobs";
        /// <summary>
        /// Route prefix for the Key Value Service endpoints
        /// </summary>
        public const string KeyValueServicePrefix = "_services/key-values";
        /// <summary>
        /// Route prefix for the Message Service endpoints
        /// </summary>
        public const string MessageServicePrefix = "_services/messages";
        /// <summary>
        /// Route prefix for the Message Queue Service endpoints
        /// </summary>
        public const string MessageQueueServicePrefix = "_services/message-queues";
        /// <summary>
        /// Route prefix for the Table Service endpoints
        /// </summary>
        public const string TableServicePrefix = "_services/tables";

        /// <summary>
        /// The Route Prefix of the Time Series Service Api
        /// </summary>
        public const string TimeSeriesServicePrefix = "_services/time-series";
    }
}
