// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Nodes;

namespace TrakHound.Api
{
    /// <summary>
    /// An Api that serves MCP Tools (mcpTools is Marked or All) (see TrakHoundApiProvider.GetMcpSources)
    /// </summary>
    /// <param name="ApiId">The ID of the Api Configuration</param>
    /// <param name="Route">The Api Route (ex. /api/example)</param>
    /// <param name="Key">Changes when the Api's Configuration or Package changes (so Tools created from the Document can be cached)</param>
    /// <param name="CreateDocument">Creates the Api's OpenAPI Document with the "x-trakhound-mcp" extension on its MCP Tools</param>
    public record TrakHoundApiMcpSource(string ApiId, string Route, string Key, Func<JsonObject> CreateDocument);
}
