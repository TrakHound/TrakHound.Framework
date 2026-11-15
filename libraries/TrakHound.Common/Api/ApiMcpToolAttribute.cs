// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Offers an Api Endpoint to AI clients as a Tool of the Instance's MCP Server (Model Context Protocol). Only mark Endpoints that are safe
    /// for an AI agent to call. The Tool is served when the Api Configuration's mcpTools is Marked (the default) or All, and the Instance's MCP Server is enabled
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ApiMcpToolAttribute : Attribute
    {
        /// <summary>
        /// The Tool name, unique within the Api (letters, numbers, '_' and '-'). The Instance prefixes it with the Api Route. Defaults to the Method name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A human readable title for the Tool
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// What the Tool does and when to use it, written for an AI agent. Defaults to the Endpoint Description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The Endpoint only reads (it doesn't change anything). Tools that aren't ReadOnly require the "write" Permission on system:mcp.
        /// Defaults to true for GET and Stream Endpoints
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>
        /// The Endpoint can delete or overwrite data, or affect equipment (clients ask before calling it). Ignored for ReadOnly Tools
        /// </summary>
        public bool Destructive { get; set; }


        /// <summary>
        /// Marks the Endpoint as an MCP Tool (ReadOnly defaults to true for GET and Stream Endpoints)
        /// </summary>
        public ApiMcpToolAttribute() { }

        /// <summary>
        /// Marks the Endpoint as an MCP Tool with the specified Description
        /// </summary>
        public ApiMcpToolAttribute(string description)
        {
            Description = description;
        }


        /// <summary>
        /// Whether the Tool only reads: ReadOnly when it was set, otherwise true for GET and Stream Endpoints
        /// </summary>
        public bool IsReadOnly(TrakHoundApiEndpointRouteAttribute endpoint)
        {
            if (ReadOnly) return true;
            return endpoint != null && (endpoint.Type == TrakHoundApiEndpointType.Stream || endpoint.Method == TrakHoundApiMethod.GET);
        }
    }
}
