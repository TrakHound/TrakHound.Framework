// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Api
{
    /// <summary>
    /// The Configuration for an Api, which loads an Api Package at a Route
    /// </summary>
    public interface ITrakHoundApiConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The display Name of the Api
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the Api
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The Route to configure the base URL for the Api
        /// </summary>
        string Route { get; }

        /// <summary>
        /// The ID of the Volume the Api uses for file storage (the Api ID is used when empty)
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// The ID of the Router the Api's Client uses to send Requests
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// The ID of the Group the Api belongs to (the Api uses the Group's Router when RouterId is empty)
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// The ID of the Package that contains the Api Controllers
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the Package to load (can contain '*' wildcards to match the latest installed Version)
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// Whether the Api is served over HTTP (it is still available within the Instance when disabled)
        /// </summary>
        bool HttpEnabled { get; }

        /// <summary>
        /// Which Endpoints are served as Tools by the Instance's MCP Server (Off, Marked with ApiMcpToolAttribute, or All). Defaults to Marked
        /// </summary>
        TrakHoundApiMcpToolsMode McpTools { get; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints to serve as MCP Tools. All when empty
        /// </summary>
        List<string> McpInclude { get; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints not to serve as MCP Tools
        /// </summary>
        List<string> McpExclude { get; }

        /// <summary>
        /// Only serve the MCP Tools that are read only (GET and Stream Endpoints, and Endpoints marked ApiMcpTool(ReadOnly = true)). Defaults to true
        /// </summary>
        bool McpReadOnly { get; }

        /// <summary>
        /// Custom Parameters that are passed to the Api Controllers
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Gets the value of the specified Parameter as a string (Secret references are resolved). Returns null if not found
        /// </summary>
        string GetParameter(string name);

        /// <summary>
        /// Gets the value of the specified Parameter converted to T (Secret references are resolved). Returns default if not found or not convertible
        /// </summary>
        T GetParameter<T>(string name);

        /// <summary>
        /// Sets (or replaces) the value of the specified Parameter. Null values are ignored
        /// </summary>
        void SetParameter(string name, object value);
    }
}
