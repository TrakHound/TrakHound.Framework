// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Instance.Api;
using TrakHound.Instances;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// An MCP Tool created from an Api Endpoint (see the Api Configuration's mcpTools)
    /// </summary>
    public class TrakHoundMcpApiEndpointTool
    {
        public string Name { get; init; }

        /// <summary>
        /// The Tool as listed to Clients
        /// </summary>
        public Tool Tool { get; init; }

        public string ApiId { get; init; }

        /// <summary>
        /// The Endpoint's Security Resource (api:{apiId}:{controllerId}:{endpointId}), authorized like an HTTP Request to the Endpoint
        /// </summary>
        public string ResourceId { get; init; }

        public bool ReadOnly { get; init; }

        public bool IsStream { get; init; }

        public TrakHoundApiMethod Method { get; init; }

        /// <summary>
        /// The Endpoint's route template including the Api Route (ex. /api/example/{key})
        /// </summary>
        public string Route { get; init; }

        /// <summary>
        /// The Route and Query Parameters (name => "path" or "query")
        /// </summary>
        public IReadOnlyList<(string Name, string In)> Parameters { get; init; }

        /// <summary>
        /// The Content Type of the Request Body (null when the Endpoint has no Body)
        /// </summary>
        public string BodyContentType { get; init; }
    }


    /// <summary>
    /// Creates MCP Tools from the Endpoints of the Apis that serve MCP Tools (mcpTools), and calls them. Tools are created from each Api's OpenAPI
    /// Document (so their Input Schemas match the Endpoint's Parameters and Body) and cached until the Api's Configuration or Package changes.
    /// Tools are read on every tools/list and tools/call request, so they follow the loaded Apis (the MCP Server is Stateless, so there are no
    /// sessions to notify when they change; Clients see the change the next time they list the Tools)
    /// </summary>
    public class TrakHoundMcpApiEndpointTools
    {
        /// <summary>
        /// Tool names are limited to 64 characters by some Clients
        /// </summary>
        private const int MaxNameLength = 64;
        private const int DefaultStreamSeconds = 10;
        private const int MaxStreamSeconds = 60;
        private const int DefaultStreamItems = 50;
        private const int MaxStreamItems = 1000;
        private const int MaxResponseLength = 100_000;
        private const string BodyArgument = "body";
        private const string SecondsArgument = "seconds";
        private const string MaxItemsArgument = "maxItems";

        private static readonly Regex _routeParameterRegex = new Regex(@"\{(\**)([^}:?=]+)[^}]*\}", RegexOptions.Compiled);
        private static readonly Regex _invalidNameRegex = new Regex(@"[^A-Za-z0-9_\-]+", RegexOptions.Compiled);
        private static readonly HashSet<string> _methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "get", "put", "post", "delete", "patch", "head", "options", "trace" };

        private readonly ITrakHoundInstance _instance;
        private readonly Dictionary<string, (string Key, List<TrakHoundMcpApiEndpointTool> Tools)> _cache = new(); // ApiId => Tools
        private readonly object _lock = new();
        private volatile ToolSet _toolSet;

        private sealed record ToolSet(string SourcesKey, IReadOnlyList<TrakHoundMcpApiEndpointTool> Tools, IReadOnlyDictionary<string, TrakHoundMcpApiEndpointTool> ByName);


        public TrakHoundMcpApiEndpointTools(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        /// <summary>
        /// The Tools of every Api that serves MCP Tools
        /// </summary>
        public IReadOnlyList<TrakHoundMcpApiEndpointTool> GetTools() => GetToolSet().Tools;

        // The Tools (and a lookup by Name), rebuilt only when an Api's Key changes, since every Tool call looks its Tool up
        private ToolSet GetToolSet()
        {
            var sources = _instance.ApiProvider?.GetMcpSources()?.ToList();
            var sourcesKey = sources.IsNullOrEmpty() ? string.Empty : string.Join("|", sources.Select(o => $"{o.ApiId}:{o.Key}"));

            var toolSet = _toolSet;
            if (toolSet != null && toolSet.SourcesKey == sourcesKey) return toolSet;

            var tools = CreateTools(sources);
            var byName = new Dictionary<string, TrakHoundMcpApiEndpointTool>();
            foreach (var tool in tools) byName.TryAdd(tool.Name, tool);

            toolSet = new ToolSet(sourcesKey, tools, byName);
            _toolSet = toolSet;
            return toolSet;
        }

        private IReadOnlyList<TrakHoundMcpApiEndpointTool> CreateTools(List<TrakHoundApiMcpSource> sources)
        {
            if (sources.IsNullOrEmpty())
            {
                lock (_lock) _cache.Clear();
                return Array.Empty<TrakHoundMcpApiEndpointTool>();
            }

            var tools = new List<TrakHoundMcpApiEndpointTool>();
            foreach (var source in sources)
            {
                List<TrakHoundMcpApiEndpointTool> apiTools = null;
                lock (_lock)
                {
                    if (_cache.TryGetValue(source.ApiId, out var cached) && cached.Key == source.Key) apiTools = cached.Tools;
                }

                if (apiTools == null)
                {
                    try
                    {
                        apiTools = CreateTools(source);
                    }
                    catch
                    {
                        apiTools = new List<TrakHoundMcpApiEndpointTool>(); // An Api whose Document can't be created has no Tools
                    }

                    lock (_lock) _cache[source.ApiId] = (source.Key, apiTools);
                }

                tools.AddRange(apiTools);
            }

            // Forget Apis that were removed (or had MCP disabled)
            lock (_lock)
            {
                foreach (var apiId in _cache.Keys.Where(o => !sources.Any(s => s.ApiId == o)).ToList()) _cache.Remove(apiId);
            }

            // Names are prefixed by the Api Route, so they only repeat when two Apis share a Route prefix (the first one is kept)
            return tools.GroupBy(o => o.Name).Select(o => o.First()).ToList();
        }

        public TrakHoundMcpApiEndpointTool GetTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return GetToolSet().ByName.GetValueOrDefault(name);
        }


        /// <summary>
        /// Calls the Endpoint (within the caller's Security Context, like an HTTP Request) and returns its Response as the Tool result
        /// </summary>
        public async Task<CallToolResult> Call(TrakHoundMcpApiEndpointTool tool, IDictionary<string, JsonElement> arguments, HttpContext httpContext, CancellationToken cancellationToken)
        {
            arguments ??= new Dictionary<string, JsonElement>();

            var client = _instance.ClientProvider.GetClient()?.WithHttpRequestSource(httpContext);
            if (client == null) return Error("The Instance is not running");

            // Route Parameters are required (a missing one would match a different Endpoint)
            string missing = null;
            var route = _routeParameterRegex.Replace(tool.Route, match =>
            {
                var catchAll = match.Groups[1].Value.Length > 0;
                var name = match.Groups[2].Value.Trim();
                var value = arguments.TryGetValue(name, out var element) ? ToText(element) : null;
                if (string.IsNullOrEmpty(value))
                {
                    missing ??= name;
                    return "";
                }
                // A catch-all Parameter ({**path}) can contain '/'
                return catchAll ? string.Join('/', value.Split('/').Select(Uri.EscapeDataString)) : Uri.EscapeDataString(value);
            });
            if (missing != null) return Error($"The '{missing}' parameter is required");

            var queryParameters = new Dictionary<string, string>();
            foreach (var parameter in tool.Parameters.Where(o => o.In == "query"))
            {
                if (arguments.TryGetValue(parameter.Name, out var element))
                {
                    var value = ToText(element);
                    if (value != null) queryParameters[parameter.Name] = value;
                }
            }
            TrakHoundApiClientParameters.Set(httpContext, queryParameters);

            if (tool.IsStream)
            {
                return await CallStream(client, tool, route, queryParameters, arguments, cancellationToken);
            }

            Stream requestBody = null;
            if (tool.BodyContentType != null && arguments.TryGetValue(BodyArgument, out var bodyElement))
            {
                // JSON Bodies are sent as the JSON value, others as the text
                var text = IsJson(tool.BodyContentType) ? bodyElement.GetRawText() : ToText(bodyElement);
                requestBody = new MemoryStream(Encoding.UTF8.GetBytes(text ?? ""));
            }

            TrakHoundApiResponse response;
            using (requestBody)
            {
                response = await client.Api.Send(tool.Method, route, requestBody, tool.BodyContentType ?? "application/octet-stream", queryParameters, cancellationToken);
            }

            try
            {
                if (!response.IsValid()) return Error($"{tool.Method.ToString().ToUpper()} {route} was not found");

                var text = $"Status Code = {response.StatusCode}";
                var content = ReadContent(response);
                if (content != null) text += $"\n\n{content}";

                return new CallToolResult
                {
                    IsError = response.StatusCode >= 400,
                    Content = new List<ContentBlock> { new TextContentBlock { Text = text } }
                };
            }
            finally
            {
                response.Content?.Dispose();
            }
        }

        private static async Task<CallToolResult> CallStream(ITrakHoundClient client, TrakHoundMcpApiEndpointTool tool, string route, Dictionary<string, string> queryParameters, IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            var seconds = Math.Clamp(GetInt(arguments, SecondsArgument) ?? DefaultStreamSeconds, 1, MaxStreamSeconds);
            var maxItems = Math.Clamp(GetInt(arguments, MaxItemsArgument) ?? DefaultStreamItems, 1, MaxStreamItems);

            var items = new List<string>();
            var full = new TaskCompletionSource();

            using var stream = await client.Api.Stream(route.TrimStart('/'), queryParameters, cancellationToken); // As the TrakHoundApiMiddleware sends it
            if (stream == null) return Error($"Stream {route} was not found");

            stream.OnReceived = response =>
            {
                var content = ReadContent(response);
                lock (items)
                {
                    if (items.Count < maxItems && content != null) items.Add(content);
                    if (items.Count >= maxItems) full.TrySetResult();
                }
            };
            stream.Start();

            await Task.WhenAny(full.Task, Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken));

            lock (items)
            {
                var text = items.Count > 0 ? string.Join("\n", items) : $"Nothing was received in {seconds} seconds";
                return new CallToolResult { Content = new List<ContentBlock> { new TextContentBlock { Text = Truncate(text) } } };
            }
        }


        private static List<TrakHoundMcpApiEndpointTool> CreateTools(TrakHoundApiMcpSource source)
        {
            var tools = new List<TrakHoundMcpApiEndpointTool>();

            var document = source.CreateDocument();
            if (document == null) return tools;

            var definitions = (document["components"] as JsonObject)?["schemas"] as JsonObject;
            var prefix = GetNamePrefix(source.Route);
            var names = new HashSet<string>(StringComparer.Ordinal);

            // Operations (Request Endpoints)
            if (document["paths"] is JsonObject paths)
            {
                foreach (var path in paths)
                {
                    if (path.Value is not JsonObject pathItem) continue;

                    foreach (var operationItem in pathItem)
                    {
                        if (!_methods.Contains(operationItem.Key) || operationItem.Value is not JsonObject operation) continue;
                        if (operation[TrakHoundApiOpenApi.McpToolExtension] is not JsonObject extension) continue;

                        var method = TrakHoundApi.GetApiMethod(operationItem.Key.ToUpperInvariant());
                        var tool = CreateTool(source, prefix, names, extension, operation, operation["parameters"] as JsonArray, operation["requestBody"] as JsonObject, definitions, path.Key, method, false);
                        if (tool != null) tools.Add(tool);
                    }
                }
            }

            // Streams
            if (document["x-trakhound-streams"] is JsonArray streams)
            {
                foreach (var stream in streams.OfType<JsonObject>())
                {
                    if (stream[TrakHoundApiOpenApi.McpToolExtension] is not JsonObject extension) continue;

                    var path = stream["path"]?.GetValue<string>() ?? "/";
                    var tool = CreateTool(source, prefix, names, extension, stream, stream["parameters"] as JsonArray, null, definitions, path, TrakHoundApiMethod.GET, true);
                    if (tool != null) tools.Add(tool);
                }
            }

            return tools;
        }

        private static TrakHoundMcpApiEndpointTool CreateTool(
            TrakHoundApiMcpSource source,
            string prefix,
            HashSet<string> names,
            JsonObject extension,
            JsonObject operation,
            JsonArray parameters,
            JsonObject requestBody,
            JsonObject definitions,
            string path,
            TrakHoundApiMethod method,
            bool isStream)
        {
            var name = GetName(prefix, extension["name"]?.GetValue<string>(), names);
            if (name == null) return null;

            var readOnly = extension["readOnly"]?.GetValue<bool>() ?? false;
            var destructive = extension["destructive"]?.GetValue<bool>() ?? false;
            var route = "/" + Url.Combine(source.Route, path).TrimStart('/');

            // Input Schema : one property per Route / Query Parameter, and "body" for the Request Body. Header Parameters are left out:
            // they carry what the caller sends with a Request (ex. its credentials for the Api), not input an agent chooses
            var properties = new JsonObject();
            var required = new JsonArray();
            var toolParameters = new List<(string Name, string In)>();

            foreach (var parameter in parameters?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var parameterName = parameter["name"]?.GetValue<string>();
                var location = parameter["in"]?.GetValue<string>();
                if (string.IsNullOrEmpty(parameterName) || (location != "path" && location != "query") || properties.ContainsKey(parameterName)) continue;

                var schema = CloneSchema(parameter["schema"]);
                var description = parameter["description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(description)) schema["description"] = description;

                properties[parameterName] = schema;
                if (parameter["required"]?.GetValue<bool>() == true) required.Add(parameterName);
                toolParameters.Add((parameterName, location));
            }

            string bodyContentType = null;
            if (requestBody?["content"] is JsonObject content && content.Count > 0)
            {
                var mediaType = content.First();
                bodyContentType = mediaType.Key;

                var schema = CloneSchema((mediaType.Value as JsonObject)?["schema"]);
                var description = requestBody["description"]?.GetValue<string>();
                schema["description"] = !string.IsNullOrEmpty(description) ? description : $"The Request Body ({bodyContentType})";

                properties[BodyArgument] = schema;
                if (requestBody["required"]?.GetValue<bool>() == true) required.Add(BodyArgument);
            }

            if (isStream)
            {
                properties[SecondsArgument] = new JsonObject { ["type"] = "integer", ["description"] = $"How long to collect items (up to {MaxStreamSeconds} seconds)", ["default"] = DefaultStreamSeconds };
                properties[MaxItemsArgument] = new JsonObject { ["type"] = "integer", ["description"] = "Return as soon as this many items have arrived", ["default"] = DefaultStreamItems };
            }

            var inputSchema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0) inputSchema["required"] = required;

            // Referenced Schemas (Body and Parameter Types) are included as $defs
            if (definitions != null && definitions.Count > 0 && inputSchema.ToJsonString().Contains("#/$defs/"))
            {
                inputSchema["$defs"] = CloneSchema(definitions);
            }

            var summary = extension["description"]?.GetValue<string>() ?? operation["summary"]?.GetValue<string>();
            var call = isStream ? $"Collects items from the Stream {route} for a number of seconds." : $"Calls {method.ToString().ToUpperInvariant()} {route}.";
            var title = extension["title"]?.GetValue<string>();

            var tool = new Tool
            {
                Name = name,
                Title = title,
                Description = string.IsNullOrEmpty(summary) ? call : $"{summary}\n\n{call}",
                InputSchema = JsonSerializer.SerializeToElement(inputSchema),
                Annotations = new ToolAnnotations
                {
                    Title = title,
                    ReadOnlyHint = readOnly,
                    DestructiveHint = readOnly ? null : destructive
                }
            };

            return new TrakHoundMcpApiEndpointTool
            {
                Name = name,
                Tool = tool,
                ApiId = source.ApiId,
                ResourceId = TrakHoundApiInformation.GetEndpointResourceId(source.ApiId, extension["controllerId"]?.GetValue<string>(), extension["endpointId"]?.GetValue<string>()),
                ReadOnly = readOnly,
                IsStream = isStream,
                Method = method,
                Route = route,
                Parameters = toolParameters,
                BodyContentType = bodyContentType
            };
        }


        /// <summary>
        /// Tool names are prefixed by the Api Route (ex. /api/example + get_value => api_example_get_value)
        /// </summary>
        private static string GetNamePrefix(string route)
        {
            var prefix = _invalidNameRegex.Replace(route ?? "", "_").Trim('_', '-');
            return string.IsNullOrEmpty(prefix) ? "api" : prefix;
        }

        private static string GetName(string prefix, string name, HashSet<string> names)
        {
            name = _invalidNameRegex.Replace(name ?? "", "_").Trim('_', '-');
            if (string.IsNullOrEmpty(name)) return null;

            var fullName = $"{prefix}_{name}";
            if (fullName.Length > MaxNameLength) fullName = fullName[^MaxNameLength..].TrimStart('_', '-');

            // Unique within the Api (ex. two Endpoints of the same Method name)
            var uniqueName = fullName;
            for (var i = 2; !names.Add(uniqueName); i++)
            {
                var suffix = $"_{i}";
                uniqueName = (fullName.Length + suffix.Length > MaxNameLength ? fullName[..(MaxNameLength - suffix.Length)] : fullName) + suffix;
            }

            return uniqueName;
        }

        /// <summary>
        /// A copy of an OpenAPI Schema with its Component references pointing at $defs
        /// </summary>
        private static JsonObject CloneSchema(JsonNode schema)
        {
            if (schema is not JsonObject) return new JsonObject();

            var json = schema.ToJsonString().Replace("\"#/components/schemas/", "\"#/$defs/");
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        private static string ToText(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;
                default: return element.GetRawText();
            }
        }

        private static int? GetInt(IDictionary<string, JsonElement> arguments, string name)
        {
            if (arguments.TryGetValue(name, out var element))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)) return value;
                if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value)) return value;
            }
            return null;
        }

        private static bool IsJson(string contentType) => contentType != null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

        private static string ReadContent(TrakHoundApiResponse response)
        {
            if (response.Content == null) return null;

            try
            {
                if (response.Content.CanSeek) response.Content.Seek(0, SeekOrigin.Begin);

                // Content is usually already in memory, so its buffer is read directly instead of being copied
                ArraySegment<byte> bytes;
                if (response.Content is MemoryStream contentMemory && contentMemory.TryGetBuffer(out var buffer))
                {
                    bytes = buffer.Slice((int)contentMemory.Position);
                }
                else
                {
                    using var memory = new MemoryStream();
                    response.Content.CopyTo(memory);
                    bytes = memory.ToArray();
                }
                if (bytes.Count == 0) return null;

                // A NUL in the first 8 KB means the Content is binary
                if (bytes.AsSpan(0, Math.Min(bytes.Count, 8192)).IndexOf((byte)0) >= 0)
                {
                    return $"({response.ContentType ?? "binary"} content, {bytes.Count:N0} bytes, not shown)";
                }

                return Truncate(Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                return null;
            }
        }

        private static string Truncate(string value)
        {
            if (value == null || value.Length <= MaxResponseLength) return value;
            return value[..MaxResponseLength] + $"... ({value.Length - MaxResponseLength:N0} more characters)";
        }

        private static CallToolResult Error(string message)
        {
            return new CallToolResult { IsError = true, Content = new List<ContentBlock> { new TextContentBlock { Text = message } } };
        }
    }
}
