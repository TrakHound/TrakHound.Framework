// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrakHound.Api;
using TrakHound.Controllers.Http;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Creates OpenAPI (3.1) Documents describing the System API, from the Instance's own Endpoints (so the Document always matches the
    /// routes that are served). Each Area (the first route segment, ex. "volumes" for /_volumes) can be described by its own Document.
    /// WebSocket (subscribe) Endpoints can't be described by OpenAPI and are listed in the "x-trakhound-streams" extension instead
    /// </summary>
    public static class TrakHoundSystemOpenApi
    {
        /// <summary>
        /// The name of the Security Scheme for Module Tokens (sent in the "X-TrakHound-Module-Token" Header)
        /// </summary>
        public const string ModuleTokenSecurityScheme = "moduleToken";

        /// <summary>
        /// The extension (on an Operation or Stream) with the System Resource and Permission that a Security Profile must grant to call it
        /// </summary>
        public const string ResourceExtension = "x-trakhound-resource";

        // The MCP Server is described by MCP itself
        private static readonly string[] _excludedPrefixes = new[] { TrakHoundInstanceRoutes.McpPrefix };


        /// <summary>
        /// Gets the Areas of the System API that have Endpoints (ex. "api", "services", "volumes")
        /// </summary>
        public static IEnumerable<string> GetAreas(EndpointDataSource endpointDataSource)
        {
            return GetEndpoints(endpointDataSource).Select(o => o.Area).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();
        }

        /// <summary>
        /// Creates the Document for the System API (only the Area's Endpoints when an Area is specified). Returns null if the Area has no Endpoints
        /// </summary>
        public static JsonObject Create(EndpointDataSource endpointDataSource, string version, string serverUrl, string area = null)
        {
            var endpoints = GetEndpoints(endpointDataSource)
                .Where(o => area == null || string.Equals(o.Area, area, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (endpoints.Count < 1) return null;

            return new Builder(version, serverUrl, area).Build(endpoints);
        }

        /// <summary>
        /// Creates the Document as indented JSON (null if the Area has no Endpoints)
        /// </summary>
        public static string CreateJson(EndpointDataSource endpointDataSource, string version, string serverUrl, string area = null)
        {
            // The System API's Endpoints don't change after startup, so each Document is built (with reflection) once. The Server URL comes
            // from the Request, so the number cached is limited (other Documents are built each time)
            var key = $"{version}|{serverUrl}|{area}";
            if (_documents.TryGetValue(key, out var document)) return document;

            document = Create(endpointDataSource, version, serverUrl, area)?.ToJsonString(_jsonOptions);
            if (document != null && _documents.Count < _maxCachedDocuments) _documents.TryAdd(key, document);

            return document;
        }

        private const int _maxCachedDocuments = 64;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _documents = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();


        private record SystemEndpoint(RouteEndpoint Endpoint, ControllerActionDescriptor Action, string Path, string Area);

        private static IEnumerable<SystemEndpoint> GetEndpoints(EndpointDataSource endpointDataSource)
        {
            if (endpointDataSource == null) yield break;

            foreach (var endpoint in endpointDataSource.Endpoints.OfType<RouteEndpoint>())
            {
                var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
                if (action == null) continue;

                var path = GetPath(endpoint.RoutePattern);
                if (!TrakHoundInstanceRoutes.IsSystemApiRoute(path)) continue;
                if (_excludedPrefixes.Any(o => path.StartsWith(o, StringComparison.OrdinalIgnoreCase))) continue;

                // ex. "/_volumes/list/files" => "volumes"
                var area = path.TrimStart('/').Split('/')[0].TrimStart('_');
                yield return new SystemEndpoint(endpoint, action, path, area);
            }
        }

        /// <summary>
        /// The OpenAPI Path of a Route Pattern (ex. "_api/package/{packageId}/{**slug}" => "/_api/package/{packageId}/{slug}")
        /// </summary>
        private static string GetPath(RoutePattern pattern)
        {
            var segments = new List<string>();
            foreach (var segment in pattern.PathSegments)
            {
                var text = string.Concat(segment.Parts.Select(part => part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart parameter => $"{{{parameter.Name}}}",
                    RoutePatternSeparatorPart separator => separator.Content,
                    _ => ""
                }));
                segments.Add(text);
            }

            return "/" + string.Join("/", segments);
        }


        private class Builder
        {
            private readonly string _version;
            private readonly string _serverUrl;
            private readonly string _area;
            private readonly TrakHoundOpenApiSchemas _schemas = new TrakHoundOpenApiSchemas();
            private readonly JsonObject _paths = new JsonObject();
            private readonly JsonArray _streams = new JsonArray();
            private readonly SortedSet<string> _tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


            public Builder(string version, string serverUrl, string area)
            {
                _version = version;
                _serverUrl = serverUrl;
                _area = area;
            }

            public JsonObject Build(IEnumerable<SystemEndpoint> endpoints)
            {
                foreach (var endpoint in endpoints.OrderBy(o => o.Path, StringComparer.OrdinalIgnoreCase))
                {
                    AddEndpoint(endpoint);
                }

                var document = new JsonObject();
                document["openapi"] = TrakHoundApiOpenApi.OpenApiVersion;

                var info = new JsonObject();
                info["title"] = _area != null ? $"TrakHound System API ({_area})" : "TrakHound System API";
                info["description"] = "Manages the TrakHound Instance (Apis, Apps, Functions, Services, Volumes, Logs, and more). Calls made with a Module Token are made as the Module, so the Module's Zone applies to them.";
                info["version"] = !string.IsNullOrEmpty(_version) ? _version : "0.0.0";
                document["info"] = info;

                if (!string.IsNullOrEmpty(_serverUrl))
                {
                    document["servers"] = new JsonArray(new JsonObject { ["url"] = _serverUrl });
                }

                var tags = new JsonArray();
                foreach (var tag in _tags) tags.Add(new JsonObject { ["name"] = tag });
                document["tags"] = tags;

                document["paths"] = _paths;

                var components = new JsonObject();
                if (_schemas.Schemas.Count > 0) components["schemas"] = _schemas.Schemas;

                // API Keys and Tokens from other Identity Providers are sent as Bearer Tokens too. Which Resources need credentials depends on the
                // Security Profiles, so every Operation accepts any of them (or none)
                var securitySchemes = new JsonObject();
                securitySchemes[TrakHoundApiOpenApi.ApiKeySecurityScheme] = new JsonObject { ["type"] = "apiKey", ["in"] = "header", ["name"] = TrakHound.Http.TrakHoundHttpConstants.ApiKeyHeader };
                securitySchemes[TrakHoundApiOpenApi.BearerSecurityScheme] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer" };
                securitySchemes[ModuleTokenSecurityScheme] = new JsonObject { ["type"] = "apiKey", ["in"] = "header", ["name"] = TrakHound.Http.TrakHoundHttpConstants.ModuleTokenHeader };
                components["securitySchemes"] = securitySchemes;
                document["components"] = components;

                document["security"] = new JsonArray(
                    new JsonObject { [TrakHoundApiOpenApi.ApiKeySecurityScheme] = new JsonArray() },
                    new JsonObject { [TrakHoundApiOpenApi.BearerSecurityScheme] = new JsonArray() },
                    new JsonObject { [ModuleTokenSecurityScheme] = new JsonArray() },
                    new JsonObject()
                    );

                if (_streams.Count > 0) document["x-trakhound-streams"] = _streams;

                return document;
            }


            private void AddEndpoint(SystemEndpoint endpoint)
            {
                var method = endpoint.Action.MethodInfo;
                var tag = GetTag(endpoint.Action);
                _tags.Add(tag);

                var httpMethods = endpoint.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                if (httpMethods.IsNullOrEmpty())
                {
                    // An attribute routed action without an HTTP Method is a WebSocket (subscribe) Endpoint. Conventional routes are called with GET
                    if (endpoint.Action.AttributeRouteInfo != null)
                    {
                        AddStream(endpoint, tag);
                        return;
                    }

                    httpMethods = new[] { "GET" };
                }

                var pathItem = _paths[endpoint.Path] as JsonObject;
                if (pathItem == null)
                {
                    pathItem = new JsonObject();
                    _paths[endpoint.Path] = pathItem;
                }

                foreach (var httpMethod in httpMethods.Select(o => o.ToLowerInvariant()))
                {
                    if (pathItem.ContainsKey(httpMethod)) continue;

                    var operation = new JsonObject();
                    operation["tags"] = new JsonArray(tag);

                    var description = method.GetCustomAttribute<RouteDescriptionAttribute>()?.Description;
                    if (!string.IsNullOrEmpty(description)) operation["summary"] = description;
                    operation["operationId"] = GetOperationId(tag, method.Name);

                    var parameters = GetParameters(endpoint, out var bodyParameter);
                    if (parameters.Count > 0) operation["parameters"] = parameters;

                    var requestBody = GetRequestBody(method, bodyParameter);
                    if (requestBody != null) operation["requestBody"] = requestBody;

                    operation["responses"] = GetResponses(method);

                    var resource = GetResource(endpoint);
                    if (resource != null) operation[ResourceExtension] = resource;

                    pathItem[httpMethod] = operation;
                }
            }

            private void AddStream(SystemEndpoint endpoint, string tag)
            {
                var method = endpoint.Action.MethodInfo;

                var stream = new JsonObject();
                stream["path"] = endpoint.Path;
                stream["operationId"] = GetOperationId(tag, method.Name);
                stream["tags"] = new JsonArray(tag);

                var description = method.GetCustomAttribute<RouteDescriptionAttribute>()?.Description;
                if (!string.IsNullOrEmpty(description)) stream["summary"] = description;

                var parameters = GetParameters(endpoint, out var bodyParameter);
                if (parameters.Count > 0) stream["parameters"] = parameters;

                // The Request Body of a Stream is sent as the first WebSocket message
                var requestBody = GetRequestBody(method, bodyParameter);
                if (requestBody != null) stream["requestBody"] = requestBody;

                var resource = GetResource(endpoint);
                if (resource != null) stream[ResourceExtension] = resource;

                _streams.Add(stream);
            }

            private JsonArray GetParameters(SystemEndpoint endpoint, out ControllerParameterDescriptor bodyParameter)
            {
                bodyParameter = null;

                var parameters = new JsonArray();
                var routeParameterNames = endpoint.Endpoint.RoutePattern.Parameters.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var descriptions = endpoint.Action.MethodInfo.GetCustomAttributes<RouteParameterDescriptionAttribute>()
                    .Where(o => o.ParameterName != null)
                    .GroupBy(o => o.ParameterName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(o => o.Key, o => o.First().Description, StringComparer.OrdinalIgnoreCase);

                foreach (var parameterDescriptor in endpoint.Action.Parameters.OfType<ControllerParameterDescriptor>())
                {
                    var parameterInfo = parameterDescriptor.ParameterInfo;
                    var parameterType = parameterDescriptor.ParameterType;
                    if (parameterType == typeof(CancellationToken)) continue;

                    var name = !string.IsNullOrEmpty(parameterDescriptor.BindingInfo?.BinderModelName) ? parameterDescriptor.BindingInfo.BinderModelName : parameterDescriptor.Name;
                    var location = GetLocation(parameterDescriptor.BindingInfo?.BindingSource, name, parameterType, routeParameterNames);
                    if (location == null) continue;

                    if (location == "body")
                    {
                        bodyParameter = parameterDescriptor;
                        continue;
                    }

                    var parameter = new JsonObject();
                    parameter["name"] = name;
                    parameter["in"] = location;
                    parameter["required"] = location == "path" || !parameterInfo.HasDefaultValue;
                    if (descriptions.TryGetValue(parameterDescriptor.Name, out var description) && !string.IsNullOrEmpty(description)) parameter["description"] = description;

                    var schema = _schemas.GetSchema(parameterType);
                    if (parameterInfo.HasDefaultValue && parameterInfo.DefaultValue != null && schema is JsonObject schemaObject && !schemaObject.ContainsKey("$ref"))
                    {
                        try { schemaObject["default"] = JsonSerializer.SerializeToNode(parameterInfo.DefaultValue, parameterInfo.DefaultValue.GetType(), _schemas.SerializerOptions); }
                        catch { }
                    }
                    parameter["schema"] = schema;
                    parameters.Add(parameter);
                }

                return parameters;
            }

            /// <summary>
            /// Where a Parameter is bound from ("path", "query", "header" or "body"). Null for Parameters the caller doesn't supply (ex. Services)
            /// </summary>
            private static string GetLocation(BindingSource bindingSource, string name, Type parameterType, HashSet<string> routeParameterNames)
            {
                if (bindingSource == BindingSource.Path) return "path";
                if (bindingSource == BindingSource.Query) return "query";
                if (bindingSource == BindingSource.Header) return "header";
                if (bindingSource == BindingSource.Body) return "body";
                if (bindingSource == BindingSource.Services || bindingSource == BindingSource.Special || bindingSource == BindingSource.FormFile) return null;

                // Not specified : the Route Value with the same Name, otherwise the Query String (or the Body for a complex Type)
                if (routeParameterNames.Contains(name)) return "path";
                return IsSimpleType(parameterType) ? "query" : "body";
            }

            private JsonObject GetRequestBody(MethodInfo method, ControllerParameterDescriptor bodyParameter)
            {
                var content = new JsonObject();
                string description = null;
                var required = false;

                if (bodyParameter != null)
                {
                    var contentType = bodyParameter.ParameterType == typeof(string) ? "text/plain" : "application/json";
                    content[contentType] = new JsonObject { ["schema"] = _schemas.GetSchema(bodyParameter.ParameterType) };
                    required = !bodyParameter.ParameterInfo.HasDefaultValue;
                }

                // Actions that read the Body themselves describe it with RouteRequestBody
                foreach (var bodyAttribute in method.GetCustomAttributes<RouteRequestBodyAttribute>())
                {
                    if (string.IsNullOrEmpty(bodyAttribute.ContentType) || content.ContainsKey(bodyAttribute.ContentType)) continue;

                    var schema = bodyAttribute.BodyType != null ? _schemas.GetSchema(bodyAttribute.BodyType) : new JsonObject { ["type"] = "string", ["format"] = "binary" };
                    content[bodyAttribute.ContentType] = new JsonObject { ["schema"] = schema };
                    description ??= bodyAttribute.Description;
                    required |= bodyAttribute.Required;
                }

                if (content.Count < 1) return null;

                var requestBody = new JsonObject();
                if (!string.IsNullOrEmpty(description)) requestBody["description"] = description;
                requestBody["required"] = required;
                requestBody["content"] = content;
                return requestBody;
            }

            private JsonObject GetResponses(MethodInfo method)
            {
                var responses = new JsonObject();

                foreach (var responseAttribute in method.GetCustomAttributes<RouteResponseAttribute>().OrderBy(o => o.StatusCode))
                {
                    var statusCode = responseAttribute.StatusCode.ToString();
                    if (responses.ContainsKey(statusCode)) continue;

                    var response = new JsonObject();
                    response["description"] = TrakHoundApiOpenApi.GetStatusDescription(responseAttribute.StatusCode);

                    if (responseAttribute.ResponseType != null)
                    {
                        var contentType = responseAttribute.ResponseType == typeof(string) ? "text/plain" : IsBinaryType(responseAttribute.ResponseType) ? "application/octet-stream" : "application/json";
                        response["content"] = new JsonObject { [contentType] = new JsonObject { ["schema"] = _schemas.GetSchema(responseAttribute.ResponseType) } };
                    }

                    responses[statusCode] = response;
                }

                if (responses.Count < 1)
                {
                    responses["200"] = new JsonObject { ["description"] = TrakHoundApiOpenApi.GetStatusDescription(200) };
                }

                return responses;
            }

            /// <summary>
            /// The System Resource (and Permission) that a Security Profile must grant. Null for Service operations, which are authorized when the Service is called
            /// </summary>
            private static JsonObject GetResource(SystemEndpoint endpoint)
            {
                var metadata = endpoint.Endpoint.Metadata;
                var resource = metadata.GetMetadata<TrakHoundSystemResourceAttribute>();
                if (resource == null) return null;

                var write = metadata.GetMetadata<TrakHoundSystemWriteAttribute>() != null;
                return new JsonObject { ["id"] = resource.ResourceId, ["permission"] = TrakHoundSystemSecurity.GetPermission(write) };
            }

            private string GetOperationId(string tag, string methodName)
            {
                var baseId = $"{tag.Replace(" ", "")}_{methodName}";
                var operationId = baseId;
                var i = 2;
                while (!_operationIds.Add(operationId))
                {
                    operationId = $"{baseId}{i++}";
                }

                return operationId;
            }

            /// <summary>
            /// ex. TrakHoundHttpKeyValuesController => "KeyValues"
            /// </summary>
            private static string GetTag(ControllerActionDescriptor action)
            {
                var name = action.ControllerName ?? action.ControllerTypeInfo.Name;
                if (name.StartsWith("TrakHoundHttp", StringComparison.Ordinal) && name.Length > "TrakHoundHttp".Length) name = name.Substring("TrakHoundHttp".Length);
                if (name.EndsWith("Controller", StringComparison.Ordinal) && name.Length > "Controller".Length) name = name.Substring(0, name.Length - "Controller".Length);
                return name;
            }

            private static bool IsSimpleType(Type type)
            {
                var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
                if (underlyingType.IsArray) return IsSimpleType(underlyingType.GetElementType());

                return underlyingType.IsPrimitive ||
                    underlyingType.IsEnum ||
                    underlyingType == typeof(string) ||
                    underlyingType == typeof(decimal) ||
                    underlyingType == typeof(DateTime) ||
                    underlyingType == typeof(DateTimeOffset) ||
                    underlyingType == typeof(TimeSpan) ||
                    underlyingType == typeof(Guid);
            }

            private static bool IsBinaryType(Type type) => type == typeof(byte[]) || typeof(Stream).IsAssignableFrom(type);
        }
    }
}
