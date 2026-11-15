// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text;
using TrakHound.Api;
using TrakHound.Instances;

namespace TrakHound.Http
{
    /// <summary>
    /// Creates the Instance's llms.txt (https://llmstxt.org) : a Markdown index for AI agents describing the Instance, how to connect
    /// (the MCP Server and API keys), and the Apis the caller can use with their Endpoints and OpenAPI Documents
    /// </summary>
    public static class TrakHoundLlmsText
    {
        public const string ContentType = "text/plain; charset=utf-8";


        /// <param name="instance">The Instance</param>
        /// <param name="apis">The Apis (and Endpoints) the caller can use, from the Api Catalog</param>
        /// <param name="baseUrl">The Instance's URL including its Base Path (ex. http://localhost:8472), so links are absolute</param>
        /// <param name="apiKeyHeader">The Header API keys are sent in</param>
        public static string Create(ITrakHoundInstance instance, IEnumerable<TrakHoundApiCatalogApi> apis, string baseUrl, string apiKeyHeader)
        {
            var configuration = instance.Configuration;
            var name = !string.IsNullOrWhiteSpace(configuration?.Name) ? configuration.Name.Trim() : "TrakHound Instance";
            baseUrl = baseUrl?.TrimEnd('/') ?? "";

            var text = new StringBuilder();
            text.AppendLine($"# {name}");
            text.AppendLine();
            text.AppendLine($"> A TrakHound Instance (ID {instance.Id}, TrakHound {instance.Version}). TrakHound hosts packages: Apis (HTTP endpoints), Apps (web pages), Functions (code run on demand or on a schedule), Workers (background processes) and Services (Blobs, Key Values, Messages, Message Queues and Tables). This file lists how to connect and the Apis you can call.");
            text.AppendLine();
            if (!string.IsNullOrWhiteSpace(configuration?.Description))
            {
                text.AppendLine(configuration.Description.Trim());
                text.AppendLine();
            }

            // Connecting
            text.AppendLine("## Connecting");
            text.AppendLine();
            if (configuration != null && configuration.SystemApiEnabled && configuration.McpEnabled)
            {
                text.AppendLine($"- [MCP Server]({baseUrl}/_mcp): a Model Context Protocol server (Streamable HTTP) with Tools to list and run Functions, query logs, Tables and Key Values, publish and wait for Messages, read Volume files, describe the Apis, and the Tools that Apis offer. It also has the Apis' OpenAPI Documents as Resources, and Prompts for common tasks.");
            }
            text.AppendLine($"- Authentication: when the Instance requires it, send an API key in the `{apiKeyHeader}` header (or `Authorization: Bearer <key>`). API keys are created in the Admin interface under Security > API Keys. What you can call depends on the key's roles; this file only lists what the current caller can use.");
            text.AppendLine($"- [Api Catalog]({baseUrl}/_developer/catalog): the Apis and Endpoints below as JSON.");
            text.AppendLine();

            // Apis
            var apiList = apis?.Where(o => o != null).OrderBy(o => o.Route, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<TrakHoundApiCatalogApi>();
            text.AppendLine("## Apis");
            text.AppendLine();
            if (apiList.Count == 0)
            {
                text.AppendLine("No Apis are available to the current caller.");
                text.AppendLine();
            }

            foreach (var api in apiList)
            {
                var route = "/" + (api.Route ?? "").Trim('/');
                var apiName = !string.IsNullOrWhiteSpace(api.Name) ? api.Name.Trim() : route;
                var description = !string.IsNullOrWhiteSpace(api.Description) ? $": {OneLine(api.Description)}" : "";

                text.AppendLine($"### {apiName}");
                text.AppendLine();
                text.AppendLine($"- [OpenAPI Document]({baseUrl}/_developer/openapi/{Uri.EscapeDataString(api.Id)}) for `{route}`{description}");

                foreach (var endpoint in api.Endpoints ?? Enumerable.Empty<TrakHoundApiCatalogEndpoint>())
                {
                    var method = endpoint.Type == TrakHoundApiEndpointType.Stream || endpoint.Method == null ? "STREAM" : endpoint.Method.ToString().ToUpperInvariant();
                    var endpointRoute = "/" + (endpoint.Route ?? route).Trim('/'); // Catalog Endpoint Routes include the Api's Route
                    var endpointDescription = !string.IsNullOrWhiteSpace(endpoint.Description) ? $": {OneLine(endpoint.Description)}" : "";
                    text.AppendLine($"  - `{method} {endpointRoute}`{endpointDescription}");
                }
                text.AppendLine();
            }

            text.AppendLine("## Optional");
            text.AppendLine();
            text.AppendLine("- Stream Endpoints (STREAM) are WebSockets, or Server-Sent Events for a GET with `Accept: text/event-stream`.");
            text.AppendLine($"- TrakHound developer documentation: {TrakHound.Instance.TrakHoundWebsite.FrameworkDocs}");

            return text.ToString();
        }

        private static string OneLine(string value) => string.Join(' ', value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
