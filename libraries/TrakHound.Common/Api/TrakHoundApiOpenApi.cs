// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using TrakHound.Packages;

namespace TrakHound.Api
{
    /// <summary>
    /// Creates an OpenAPI (3.1) Document describing the Endpoints of an API (the Controllers loaded for an API Configuration).
    /// Built by reflection using the same rules the Instance uses to Route and Bind Requests.
    /// Stream Endpoints (WebSocket) can't be described by OpenAPI and are listed in the "x-trakhound-streams" extension instead
    /// </summary>
    public static class TrakHoundApiOpenApi
    {
        /// <summary>
        /// The OpenAPI Specification Version of the generated Documents
        /// </summary>
        public const string OpenApiVersion = "3.1.0";

        /// <summary>
        /// The name of the Security Scheme for API Keys (sent in a Header)
        /// </summary>
        public const string ApiKeySecurityScheme = "apiKey";

        /// <summary>
        /// The name of the Security Scheme for Bearer Tokens
        /// </summary>
        public const string BearerSecurityScheme = "bearer";

        private static readonly Regex _routeParameterRegex = new Regex(@"\{\**([^}:?=]+)[^}]*\}", RegexOptions.Compiled);
        /// <summary>
        /// The extension (on an Operation or Stream) describing its MCP Tool (see TrakHoundApiOpenApiOptions.McpTools)
        /// </summary>
        public const string McpToolExtension = "x-trakhound-mcp";

        /// <summary>
        /// The Method that Stream Endpoints are matched by in MCP Include / Exclude patterns (ex. "STREAM *")
        /// </summary>
        public const string McpStreamMethod = "STREAM";


        /// <summary>
        /// Creates the OpenAPI Document for the specified Controller Types. Returns null if the Configuration is null or there are no Controller Types
        /// </summary>
        public static JsonObject Create(ITrakHoundApiConfiguration configuration, TrakHoundPackage package, IEnumerable<Type> controllerTypes, TrakHoundApiOpenApiOptions options = null)
        {
            if (configuration == null || controllerTypes.IsNullOrEmpty()) return null;

            return new Builder(configuration, package, options ?? new TrakHoundApiOpenApiOptions()).Build(controllerTypes);
        }

        /// <summary>
        /// Creates the OpenAPI Document for the specified Controller Types as indented JSON (null if it could not be created)
        /// </summary>
        public static string CreateJson(ITrakHoundApiConfiguration configuration, TrakHoundPackage package, IEnumerable<Type> controllerTypes, TrakHoundApiOpenApiOptions options = null)
        {
            return Create(configuration, package, controllerTypes, options)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }


        private class Builder
        {
            private readonly ITrakHoundApiConfiguration _configuration;
            private readonly TrakHoundPackage _package;
            private readonly TrakHoundApiOpenApiOptions _options;

            // Created for each Document (the Type cache would otherwise keep Module Types loaded after their Module is unloaded)
            private readonly TrakHoundOpenApiSchemas _schemaBuilder = new TrakHoundOpenApiSchemas();

            private readonly JsonObject _paths = new JsonObject();
            private readonly Dictionary<string, (string Path, List<string> ParameterNames)> _pathSignatures = new Dictionary<string, (string, List<string>)>();
            private readonly Dictionary<string, string> _tags = new Dictionary<string, string>();
            private readonly HashSet<string> _operationIds = new HashSet<string>();
            private readonly JsonArray _streams = new JsonArray();


            public Builder(ITrakHoundApiConfiguration configuration, TrakHoundPackage package, TrakHoundApiOpenApiOptions options)
            {
                _configuration = configuration;
                _package = package;
                _options = options;
            }

            public JsonObject Build(IEnumerable<Type> controllerTypes)
            {
                foreach (var controllerType in controllerTypes.Where(o => o != null).Distinct())
                {
                    AddController(controllerType);
                }

                var document = new JsonObject();
                document["openapi"] = OpenApiVersion;

                var info = new JsonObject();
                info["title"] = !string.IsNullOrEmpty(_configuration.Name) ? _configuration.Name : _configuration.PackageId ?? _configuration.Id;
                if (!string.IsNullOrEmpty(_configuration.Description)) info["description"] = _configuration.Description;
                info["version"] = _package?.Version ?? _configuration.PackageVersion ?? "0.0.0";
                document["info"] = info;

                // Relative to the URL the Document was requested from (so it works for any host the Instance is reached by)
                var server = new JsonObject();
                server["url"] = !string.IsNullOrEmpty(_options.ServerUrl) ? _options.ServerUrl : NormalizeRoute(_configuration.Route);
                document["servers"] = new JsonArray(server);

                if (_tags.Count > 0)
                {
                    var tags = new JsonArray();
                    foreach (var tag in _tags.OrderBy(o => o.Key))
                    {
                        var tagObject = new JsonObject();
                        tagObject["name"] = tag.Key;
                        if (!string.IsNullOrEmpty(tag.Value)) tagObject["description"] = tag.Value;
                        tags.Add(tagObject);
                    }
                    document["tags"] = tags;
                }

                document["paths"] = _paths;

                var components = new JsonObject();
                if (_schemaBuilder.Schemas.Count > 0) components["schemas"] = _schemaBuilder.Schemas;

                if (_options.IncludeSecuritySchemes)
                {
                    // API Keys issued by the Instance are sent in a Header, or as a Bearer Token (as are Tokens from other Identity Providers)
                    var securitySchemes = new JsonObject();
                    securitySchemes[ApiKeySecurityScheme] = new JsonObject { ["type"] = "apiKey", ["in"] = "header", ["name"] = _options.ApiKeyHeader };
                    securitySchemes[BearerSecurityScheme] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer" };
                    components["securitySchemes"] = securitySchemes;
                }

                if (components.Count > 0) document["components"] = components;

                var extension = new JsonObject();
                extension["apiId"] = _configuration.Id;
                if (_options.IncludePackageDetails)
                {
                    extension["packageId"] = _configuration.PackageId;
                    if (_package != null)
                    {
                        extension["packageVersion"] = _package.Version;
                        extension["packageHash"] = _package.Hash;
                    }
                }
                document["x-trakhound"] = extension;

                if (_streams.Count > 0) document["x-trakhound-streams"] = _streams;

                return document;
            }


            private void AddController(Type controllerType)
            {
                var controllerRoute = "/";
                var controllerName = controllerType.Name;
                string controllerDescription = null;

                var controllerAttribute = controllerType.GetCustomAttribute<TrakHoundApiControllerAttribute>();
                if (controllerAttribute != null)
                {
                    if (!string.IsNullOrEmpty(controllerAttribute.Route)) controllerRoute = controllerAttribute.Route;
                    if (!string.IsNullOrEmpty(controllerAttribute.Name)) controllerName = controllerAttribute.Name;
                    controllerDescription = controllerAttribute.Description;
                }

                foreach (var method in controllerType.GetMethods().Where(TrakHoundApi.IsEndpointMethod))
                {
                    var routeAttributes = method.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>();
                    if (_options.EndpointFilter != null) routeAttributes = routeAttributes?.Where(o => _options.EndpointFilter(controllerType, o)).ToList();
                    if (routeAttributes.IsNullOrEmpty()) continue;

                    var groupAttribute = method.GetCustomAttribute<TrakHoundApiGroupAttribute>();
                    var tag = !string.IsNullOrEmpty(groupAttribute?.Name) ? groupAttribute.Name : controllerName;
                    if (!_tags.ContainsKey(tag)) _tags[tag] = tag == controllerName ? controllerDescription : null;

                    foreach (var routeAttribute in routeAttributes)
                    {
                        var route = NormalizeRoute(Url.Combine(controllerRoute, routeAttribute.Route));

                        if (routeAttribute.Type == TrakHoundApiEndpointType.Stream || routeAttribute.Method == null)
                        {
                            var stream = new JsonObject();
                            stream["path"] = route;
                            stream["operationId"] = GetOperationId(method);
                            if (!string.IsNullOrEmpty(routeAttribute.Description)) stream["summary"] = routeAttribute.Description;

                            // The Stream's Parameters (bound by MCP Tools, and used by clients such as the debug host's Api Debugger to connect)
                            var streamParameters = new JsonArray();
                            AddRouteParameters(streamParameters, method, route);
                            AddQueryParameters(streamParameters, method);
                            AddHeaderParameters(streamParameters, method);
                            if (streamParameters.Count > 0) stream["parameters"] = streamParameters;

                            var streamMcpTool = GetMcpTool(controllerType, method, routeAttribute, route);
                            if (streamMcpTool != null) stream[McpToolExtension] = streamMcpTool;

                            _streams.Add(stream);
                            continue;
                        }

                        AddOperation(controllerType, method, routeAttribute, route, tag);
                    }
                }
            }

            private void AddOperation(Type controllerType, MethodInfo method, TrakHoundApiEndpointRouteAttribute routeAttribute, string route, string tag)
            {
                var routeParameterNames = new List<string>();
                var path = _routeParameterRegex.Replace(route, match =>
                {
                    var name = match.Groups[1].Value.Trim();
                    routeParameterNames.Add(name);
                    return $"{{{name}}}";
                });

                // OpenAPI Paths must be unique ignoring Parameter Names (ex. "/{organization}/{uuid}" and "/{organization}/{packageId}").
                // Route Parameters are matched by position, so use the first Path's Names (Method Parameters are still found by their own Name)
                var signature = _routeParameterRegex.Replace(route, "{}");
                var documentParameterNames = routeParameterNames;
                if (_pathSignatures.TryGetValue(signature, out var existing))
                {
                    path = existing.Path;
                    documentParameterNames = existing.ParameterNames;
                }
                else
                {
                    _pathSignatures[signature] = (path, routeParameterNames);
                }

                var pathItem = _paths[path] as JsonObject;
                if (pathItem == null)
                {
                    pathItem = new JsonObject();
                    _paths[path] = pathItem;
                }

                var httpMethod = routeAttribute.Method.Value.ToString().ToLower();
                if (pathItem.ContainsKey(httpMethod)) return; // The first matching Endpoint handles the Request

                var operation = new JsonObject();
                operation["tags"] = new JsonArray(tag);
                if (!string.IsNullOrEmpty(routeAttribute.Description)) operation["summary"] = routeAttribute.Description;
                operation["operationId"] = GetOperationId(method);

                var parameterDescriptions = GetParameterDescriptions(method);

                var methodParameters = method.GetParameters();
                var parameters = new JsonArray();

                // Route Parameters (bound by the Method Parameter Name)
                var addedRouteParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < routeParameterNames.Count; i++)
                {
                    var routeParameterName = routeParameterNames[i];
                    var documentParameterName = i < documentParameterNames.Count ? documentParameterNames[i] : routeParameterName;
                    if (!addedRouteParameters.Add(documentParameterName)) continue;

                    var methodParameter = methodParameters.FirstOrDefault(o =>
                        o.GetCustomAttribute<FromRouteAttribute>() != null &&
                        string.Equals(o.Name, routeParameterName, StringComparison.OrdinalIgnoreCase));

                    var parameter = new JsonObject();
                    parameter["name"] = documentParameterName;
                    parameter["in"] = "path";
                    parameter["required"] = true; // OpenAPI requires Path Parameters
                    var description = methodParameter != null ? parameterDescriptions.GetValueOrDefault(methodParameter.Name) : null;
                    if (!string.IsNullOrEmpty(description)) parameter["description"] = description;
                    parameter["schema"] = methodParameter != null ? _schemaBuilder.GetSchema(methodParameter.ParameterType) : new JsonObject { ["type"] = "string" };
                    parameters.Add(parameter);
                }

                AddQueryParameters(parameters, method);
                AddHeaderParameters(parameters, method);

                if (parameters.Count > 0) operation["parameters"] = parameters;

                // Request Body (the Instance binds the last FromBody Parameter)
                var bodyParameter = methodParameters.LastOrDefault(o => o.GetCustomAttribute<FromBodyAttribute>() != null);
                if (bodyParameter != null)
                {
                    var bodyAttribute = bodyParameter.GetCustomAttribute<FromBodyAttribute>();
                    var contentType = !string.IsNullOrEmpty(bodyAttribute.ContentType) ? bodyAttribute.ContentType : FromBodyAttribute.GetDefaultContentType(bodyParameter.ParameterType);

                    var mediaType = new JsonObject();
                    mediaType["schema"] = _schemaBuilder.GetSchema(bodyParameter.ParameterType);

                    var content = new JsonObject();
                    content[contentType] = mediaType;

                    var requestBody = new JsonObject();
                    var description = parameterDescriptions.GetValueOrDefault(bodyParameter.Name);
                    if (!string.IsNullOrEmpty(description)) requestBody["description"] = description;
                    requestBody["required"] = !bodyParameter.HasDefaultValue;
                    requestBody["content"] = content;
                    operation["requestBody"] = requestBody;
                }

                operation["responses"] = GetResponses(method);

                var mcpTool = GetMcpTool(controllerType, method, routeAttribute, route);
                if (mcpTool != null) operation[McpToolExtension] = mcpTool;

                // The Credentials the caller can use for the Endpoint (an empty Requirement means it can also be called without any)
                if (_options.IncludeSecuritySchemes && _options.EndpointSecurity != null)
                {
                    var authenticationRequired = _options.EndpointSecurity(controllerType, routeAttribute);
                    if (authenticationRequired.HasValue)
                    {
                        var requirements = new JsonArray();
                        requirements.Add(new JsonObject { [ApiKeySecurityScheme] = new JsonArray() });
                        requirements.Add(new JsonObject { [BearerSecurityScheme] = new JsonArray() });
                        if (!authenticationRequired.Value) requirements.Add(new JsonObject());
                        operation["security"] = requirements;
                    }
                }

                pathItem[httpMethod] = operation;
            }

            /// <summary>
            /// Query Parameters (bound by the FromQuery Name, or the Method Parameter Name)
            /// </summary>
            private void AddQueryParameters(JsonArray parameters, MethodInfo method)
            {
                var parameterDescriptions = GetParameterDescriptions(method);

                foreach (var methodParameter in method.GetParameters())
                {
                    var queryAttribute = methodParameter.GetCustomAttribute<FromQueryAttribute>();
                    if (queryAttribute == null) continue;

                    var name = !string.IsNullOrEmpty(queryAttribute.ParameterName) ? queryAttribute.ParameterName : methodParameter.Name;

                    // Reserved Client Parameters are set by the Instance and can't be supplied by the caller
                    if (TrakHoundApi.IsReservedParameter(name)) continue;

                    var parameter = new JsonObject();
                    parameter["name"] = name;
                    parameter["in"] = "query";
                    parameter["required"] = !methodParameter.HasDefaultValue;
                    var description = parameterDescriptions.GetValueOrDefault(methodParameter.Name);
                    if (!string.IsNullOrEmpty(description)) parameter["description"] = description;

                    var schema = _schemaBuilder.GetSchema(methodParameter.ParameterType);
                    if (methodParameter.HasDefaultValue && methodParameter.DefaultValue != null && schema is JsonObject schemaObject && !schemaObject.ContainsKey("$ref"))
                    {
                        try { schemaObject["default"] = JsonSerializer.SerializeToNode(methodParameter.DefaultValue, methodParameter.DefaultValue.GetType(), _schemaBuilder.SerializerOptions); }
                        catch { }
                    }
                    parameter["schema"] = schema;
                    parameters.Add(parameter);
                }
            }

            /// <summary>
            /// Header Parameters (bound by the FromHeader Name, or the Method Parameter Name)
            /// </summary>
            private void AddHeaderParameters(JsonArray parameters, MethodInfo method)
            {
                var parameterDescriptions = GetParameterDescriptions(method);

                foreach (var methodParameter in method.GetParameters())
                {
                    var headerAttribute = methodParameter.GetCustomAttribute<FromHeaderAttribute>();
                    if (headerAttribute == null) continue;

                    var parameter = new JsonObject();
                    parameter["name"] = FromHeaderAttribute.GetHeaderName(headerAttribute, methodParameter.Name);
                    parameter["in"] = "header";
                    parameter["required"] = !methodParameter.HasDefaultValue;
                    var description = parameterDescriptions.GetValueOrDefault(methodParameter.Name);
                    if (!string.IsNullOrEmpty(description)) parameter["description"] = description;
                    parameter["schema"] = _schemaBuilder.GetSchema(methodParameter.ParameterType);
                    parameters.Add(parameter);
                }
            }

            /// <summary>
            /// Route Parameters of a Stream (Operations add theirs in AddOperation, which also merges Paths that differ only by Parameter Names)
            /// </summary>
            private void AddRouteParameters(JsonArray parameters, MethodInfo method, string route)
            {
                var parameterDescriptions = GetParameterDescriptions(method);
                var methodParameters = method.GetParameters();
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in _routeParameterRegex.Matches(route))
                {
                    var name = match.Groups[1].Value.Trim();
                    if (!added.Add(name)) continue;

                    var methodParameter = methodParameters.FirstOrDefault(o =>
                        o.GetCustomAttribute<FromRouteAttribute>() != null &&
                        string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

                    var parameter = new JsonObject();
                    parameter["name"] = name;
                    parameter["in"] = "path";
                    parameter["required"] = true;
                    var description = methodParameter != null ? parameterDescriptions.GetValueOrDefault(methodParameter.Name) : null;
                    if (!string.IsNullOrEmpty(description)) parameter["description"] = description;
                    parameter["schema"] = methodParameter != null ? _schemaBuilder.GetSchema(methodParameter.ParameterType) : new JsonObject { ["type"] = "string" };
                    parameters.Add(parameter);
                }
            }

            private static Dictionary<string, string> GetParameterDescriptions(MethodInfo method)
            {
                // Method Parameter Name => Description (from its FromQuery, FromRoute, FromHeader, or FromBody attribute)
                return method.GetParameters()
                    .Select(o => (o.Name, Description: TrakHoundApi.GetParameterDescription(o)))
                    .Where(o => o.Name != null && o.Description != null)
                    .ToDictionary(o => o.Name, o => o.Description);
            }

            /// <summary>
            /// The "x-trakhound-mcp" extension of an Endpoint that is an MCP Tool (see TrakHoundApiOpenApiOptions.McpTools). Null when it isn't
            /// </summary>
            private JsonObject GetMcpTool(Type controllerType, MethodInfo method, TrakHoundApiEndpointRouteAttribute routeAttribute, string route)
            {
                if (_options.McpTools == TrakHoundApiMcpToolsMode.Off) return null;

                var mcpAttribute = method.GetCustomAttribute<ApiMcpToolAttribute>();
                if (mcpAttribute == null && _options.McpTools != TrakHoundApiMcpToolsMode.All) return null;

                // Patterns (ex. "/orders/*", "GET *" or "POST /orders/search")
                if (!_options.McpInclude.IsNullOrEmpty() && !_options.McpInclude.Any(o => IsEndpointMatch(routeAttribute, route, o))) return null;
                if (!_options.McpExclude.IsNullOrEmpty() && _options.McpExclude.Any(o => IsEndpointMatch(routeAttribute, route, o))) return null;

                // Endpoints that aren't marked are read only when they are GET or Stream Endpoints, and are otherwise treated as Destructive (Clients ask first)
                var readOnly = mcpAttribute != null ? mcpAttribute.IsReadOnly(routeAttribute) : new ApiMcpToolAttribute().IsReadOnly(routeAttribute);
                if (_options.McpReadOnly && !readOnly) return null;
                var destructive = !readOnly && (mcpAttribute == null || mcpAttribute.Destructive);

                var tool = new JsonObject();
                tool["name"] = !string.IsNullOrEmpty(mcpAttribute?.Name) ? mcpAttribute.Name : method.Name;
                if (!string.IsNullOrEmpty(mcpAttribute?.Title)) tool["title"] = mcpAttribute.Title;
                var description = !string.IsNullOrEmpty(mcpAttribute?.Description) ? mcpAttribute.Description : routeAttribute.Description;
                if (!string.IsNullOrEmpty(description)) tool["description"] = description;
                tool["readOnly"] = readOnly;
                tool["destructive"] = destructive;
                tool["controllerId"] = TrakHoundApiInformation.GetControllerId(_configuration.Id, controllerType);
                tool["endpointId"] = TrakHoundApiInformationFactory.GetEndpointId(routeAttribute);
                return tool;
            }

            /// <summary>
            /// Whether an Endpoint matches a pattern : an optional Method list followed by a space (ex. "GET ", "GET,HEAD ", or "STREAM " for
            /// Stream Endpoints), then a Route pattern ('*' matches anything, compared without leading or trailing '/'). Without a Method list,
            /// any Method matches
            /// </summary>
            private static bool IsEndpointMatch(TrakHoundApiEndpointRouteAttribute routeAttribute, string route, string pattern)
            {
                if (string.IsNullOrWhiteSpace(pattern)) return false;

                var routePattern = pattern.Trim();

                var separatorIndex = routePattern.IndexOfAny(new[] { ' ', '\t' });
                if (separatorIndex > 0)
                {
                    var methods = routePattern[..separatorIndex].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (methods.Length > 0 && methods.All(IsMethodName))
                    {
                        var endpointMethod = GetMcpMethodName(routeAttribute);
                        if (!methods.Any(o => string.Equals(o, endpointMethod, StringComparison.OrdinalIgnoreCase))) return false;

                        routePattern = routePattern[(separatorIndex + 1)..].Trim();
                    }
                }

                return IsRouteMatch(route, routePattern);
            }

            /// <summary>
            /// The Method an Endpoint is matched by in MCP patterns (ex. "GET", or "STREAM" for a Stream Endpoint)
            /// </summary>
            private static string GetMcpMethodName(TrakHoundApiEndpointRouteAttribute routeAttribute)
            {
                if (routeAttribute.Type == TrakHoundApiEndpointType.Stream || routeAttribute.Method == null) return McpStreamMethod;
                return routeAttribute.Method.Value.ToString();
            }

            private static bool IsMethodName(string value)
            {
                return string.Equals(value, McpStreamMethod, StringComparison.OrdinalIgnoreCase) ||
                    Enum.TryParse<TrakHoundApiMethod>(value, true, out var method) && Enum.IsDefined(method) && !int.TryParse(value, out _);
            }

            /// <summary>
            /// Whether an Endpoint Route (ex. /orders/{id}) matches a Route pattern ('*' matches anything, compared without leading or trailing '/')
            /// </summary>
            private static bool IsRouteMatch(string route, string pattern)
            {
                if (string.IsNullOrWhiteSpace(pattern)) return false;

                var normalizedRoute = (route ?? "").Trim().Trim('/');
                var normalizedPattern = pattern.Trim().Trim('/');
                var regex = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(normalizedRoute, regex, RegexOptions.IgnoreCase);
            }

            private JsonObject GetResponses(MethodInfo method)
            {
                var responses = new JsonObject();

                foreach (var responseAttribute in method.GetCustomAttributes<TrakHoundApiResponseAttribute>().OrderBy(o => o.StatusCode))
                {
                    var statusCode = responseAttribute.StatusCode.ToString();
                    if (responses.ContainsKey(statusCode)) continue;

                    var response = new JsonObject();
                    response["description"] = GetStatusDescription(responseAttribute.StatusCode);

                    if (responseAttribute.ReturnType != null)
                    {
                        var contentType = FromBodyAttribute.GetDefaultContentType(responseAttribute.ReturnType);

                        var mediaType = new JsonObject();
                        mediaType["schema"] = _schemaBuilder.GetSchema(responseAttribute.ReturnType);

                        var content = new JsonObject();
                        content[contentType] = mediaType;
                        response["content"] = content;
                    }

                    responses[statusCode] = response;
                }

                // Response Types aren't known from the Method (TrakHoundApiResponse), so describe a Success response without content
                if (responses.Count < 1)
                {
                    responses["200"] = new JsonObject { ["description"] = GetStatusDescription(200) };
                }

                return responses;
            }

            private string GetOperationId(MethodInfo method)
            {
                var operationId = method.Name;
                var i = 2;
                while (!_operationIds.Add(operationId))
                {
                    operationId = $"{method.Name}{i++}";
                }

                return operationId;
            }
        }


        /// <summary>
        /// ex. "/api/packages" (null or empty is "/")
        /// </summary>
        private static string NormalizeRoute(string route)
        {
            route = route?.Trim();
            if (string.IsNullOrEmpty(route)) return "/";

            if (!route.StartsWith('/')) route = '/' + route;
            if (route.Length > 1) route = route.TrimEnd('/');

            return route;
        }

        /// <summary>
        /// The description of an HTTP Status Code (ex. 404 => "Not Found")
        /// </summary>
        public static string GetStatusDescription(int statusCode)
        {
            if (Enum.IsDefined(typeof(HttpStatusCode), statusCode))
            {
                // ex. NotFound => Not Found
                var name = ((HttpStatusCode)statusCode).ToString();
                var description = new StringBuilder();
                for (var i = 0; i < name.Length; i++)
                {
                    if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) description.Append(' ');
                    description.Append(name[i]);
                }

                return description.ToString();
            }

            return statusCode.ToString();
        }
    }


    /// <summary>
    /// Options for creating an OpenAPI Document
    /// </summary>
    public class TrakHoundApiOpenApiOptions
    {
        /// <summary>
        /// Returns false to leave an Endpoint (Controller Type and Endpoint Attribute) out of the Document. All Endpoints are included when null
        /// </summary>
        public Func<Type, TrakHoundApiEndpointRouteAttribute, bool> EndpointFilter { get; set; }

        /// <summary>
        /// Whether an Endpoint (Controller Type and Endpoint Attribute) requires Credentials (true), accepts them optionally (false),
        /// or doesn't describe its Security (null). Only used when IncludeSecuritySchemes is true
        /// </summary>
        public Func<Type, TrakHoundApiEndpointRouteAttribute, bool?> EndpointSecurity { get; set; }

        /// <summary>
        /// The URL of the Server (defaults to the Api Route, relative to the URL the Document was requested from)
        /// </summary>
        public string ServerUrl { get; set; }

        /// <summary>
        /// Whether to describe the API Key and Bearer Token Security Schemes
        /// </summary>
        public bool IncludeSecuritySchemes { get; set; }

        /// <summary>
        /// The Header that API Keys are sent in (used when IncludeSecuritySchemes is true)
        /// </summary>
        public string ApiKeyHeader { get; set; } = "X-API-Key";

        /// <summary>
        /// Whether to include the Package ID, Version, and Hash in the "x-trakhound" extension
        /// </summary>
        public bool IncludePackageDetails { get; set; } = true;

        /// <summary>
        /// Which Endpoints to describe in the "x-trakhound-mcp" extension of their Operation (or Stream) : none (Off), the ones marked with
        /// ApiMcpToolAttribute (Marked), or every Endpoint (All). Used by the Instance's MCP Server to create Tools
        /// </summary>
        public TrakHoundApiMcpToolsMode McpTools { get; set; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints to describe as MCP Tools. All when empty
        /// </summary>
        public IEnumerable<string> McpInclude { get; set; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints not to describe as MCP Tools
        /// </summary>
        public IEnumerable<string> McpExclude { get; set; }

        /// <summary>
        /// Only describe the MCP Tools that are read only (GET and Stream Endpoints, and Endpoints marked ApiMcpTool(ReadOnly = true))
        /// </summary>
        public bool McpReadOnly { get; set; }
    }
}
