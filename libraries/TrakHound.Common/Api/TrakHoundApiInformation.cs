// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing a loaded Api, its Package, and its Controllers
    /// </summary>
    public class TrakHoundApiInformation
    {
        /// <summary>
        /// The ID of the Api Configuration
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The base Route of the Api (always starts with "/")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The ID of the Volume the Api uses for file storage
        /// </summary>
        [JsonPropertyName("volumeId")]
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Group the Api belongs to
        /// </summary>
        [JsonPropertyName("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Api
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that is loaded
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// Whether the Api is served over HTTP
        /// </summary>
        [JsonPropertyName("httpEnabled")]
        public bool HttpEnabled { get; set; }

        /// <summary>
        /// Which Endpoints are served as MCP Tools (Off, Marked with ApiMcpToolAttribute, or All)
        /// </summary>
        [JsonPropertyName("mcpTools")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TrakHoundApiMcpToolsMode McpTools { get; set; }

        /// <summary>
        /// Whether only the read only MCP Tools are served
        /// </summary>
        [JsonPropertyName("mcpReadOnly")]
        public bool McpReadOnly { get; set; }

        /// <summary>
        /// The date the Package was built
        /// </summary>
        [JsonPropertyName("packageBuildDate")]
        public DateTime PackageBuildDate { get; set; }

        /// <summary>
        /// The hash of the Package
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// The Icon (Image Metadata) of the Package
        /// </summary>
        [JsonPropertyName("packageIcon")]
        public string PackageIcon { get; set; }

        /// <summary>
        /// The contents of the Package's README
        /// </summary>
        [JsonPropertyName("packageReadMe")]
        public string PackageReadMe { get; set; }

        /// <summary>
        /// The source Repository the Package was built from (from the Package Metadata)
        /// </summary>
        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        /// <summary>
        /// The Branch of the source Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        /// <summary>
        /// The Directory within the source Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryDirectory")]
        public string RepositoryDirectory { get; set; }

        /// <summary>
        /// The Commit of the source Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// The TrakHound Version the Package was built against (from the Package Metadata)
        /// </summary>
        [JsonPropertyName("trakhoundVersion")]
        public string TrakHoundVersion { get; set; }

        /// <summary>
        /// The Controllers loaded for the Api
        /// </summary>
        [JsonPropertyName("controllers")]
        public IEnumerable<TrakHoundApiControllerInformation> Controllers { get; set; }



        /// <summary>
        /// The Identifier of a Controller (an MD5 hash of the Api Configuration ID and the Controller Type Name)
        /// </summary>
        public static string GetControllerId(string configurationId, Type controllerType)
        {
            return $"{configurationId}:{controllerType?.Name}".ToMD5Hash();
        }

        /// <summary>
        /// The Identifier of an Endpoint from the Method (null for Stream Endpoints) and Route of its Endpoint Attribute
        /// </summary>
        public static string GetEndpointId(TrakHoundApiMethod? method, string route)
        {
            return $"{method.ToString()}:{route}".ToMD5Hash();
        }

        /// <summary>
        /// The Security Resource ID of an Endpoint (ex. "api:{apiId}:{controllerId}:{endpointId}")
        /// </summary>
        public static string GetEndpointResourceId(string apiId, string controllerId, string endpointId)
        {
            return $"api:{apiId}:{controllerId}:{endpointId}";
        }
    }
}
