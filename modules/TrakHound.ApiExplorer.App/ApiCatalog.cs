// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TrakHound.ApiExplorer.App
{
    /// <summary>
    /// The Api Catalog returned by the Instance (/_developer/catalog). Only includes the Endpoints the caller can use
    /// </summary>
    public class ApiCatalog
    {
        public const string CatalogPath = "_developer/catalog";
        public const string OpenApiPath = "_developer/openapi";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };


        [JsonPropertyName("identity")]
        public ApiCatalogIdentity Identity { get; set; }

        [JsonPropertyName("signInProviders")]
        public List<ApiCatalogSignInProvider> SignInProviders { get; set; }

        [JsonPropertyName("apiKeyHeader")]
        public string ApiKeyHeader { get; set; }

        [JsonPropertyName("apis")]
        public List<ApiCatalogApi> Apis { get; set; }


        public static ApiCatalog Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var catalog = JsonSerializer.Deserialize<ApiCatalog>(json, _jsonOptions);
                if (catalog != null)
                {
                    catalog.Apis ??= new List<ApiCatalogApi>();
                    foreach (var api in catalog.Apis)
                    {
                        api.Endpoints ??= new List<ApiCatalogEndpoint>();
                        foreach (var endpoint in api.Endpoints)
                        {
                            endpoint.Api = api;
                            endpoint.Parameters ??= new List<ApiCatalogParameter>();
                            endpoint.Responses ??= new List<ApiCatalogResponse>();
                        }
                    }
                }
                return catalog;
            }
            catch
            {
                return null;
            }
        }
    }

    public class ApiCatalogIdentity
    {
        [JsonPropertyName("providerId")]
        public string ProviderId { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : UserId;
    }

    public class ApiCatalogSignInProvider
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("signInPath")]
        public string SignInPath { get; set; }
    }

    public class ApiCatalogApi
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("route")]
        public string Route { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("endpoints")]
        public List<ApiCatalogEndpoint> Endpoints { get; set; }
    }

    public class ApiCatalogEndpoint
    {
        private static readonly Regex _routeParameterRegex = new Regex(@"\{(\**)([^}:?=]+)[^}]*\}", RegexOptions.Compiled);


        [JsonIgnore]
        public ApiCatalogApi Api { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("controllerId")]
        public string ControllerId { get; set; }

        [JsonPropertyName("controller")]
        public string Controller { get; set; }

        [JsonPropertyName("controllerDescription")]
        public string ControllerDescription { get; set; }

        [JsonPropertyName("group")]
        public string Group { get; set; }

        /// <summary>
        /// "Request" or "Stream"
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("route")]
        public string Route { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public List<ApiCatalogParameter> Parameters { get; set; }

        [JsonPropertyName("responses")]
        public List<ApiCatalogResponse> Responses { get; set; }

        [JsonPropertyName("authenticationRequired")]
        public bool AuthenticationRequired { get; set; }


        public bool IsStream => string.Equals(Type, "Stream", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The HTTP Method, or "WS" for a Stream (WebSocket)
        /// </summary>
        public string MethodLabel => IsStream ? "WS" : (Method ?? "GET").ToUpperInvariant();

        /// <summary>
        /// The Group used to organize the Endpoint (the Controller when it has no Group)
        /// </summary>
        public string GroupName => !string.IsNullOrEmpty(Group) ? Group : Controller;

        public IEnumerable<ApiCatalogParameter> RouteParameters => Parameters.Where(o => o.IsRoute);

        public IEnumerable<ApiCatalogParameter> QueryParameters => Parameters.Where(o => o.IsQuery);

        public IEnumerable<ApiCatalogParameter> HeaderParameters => Parameters.Where(o => o.IsHeader);

        public ApiCatalogParameter BodyParameter => Parameters.LastOrDefault(o => o.IsBody);


        public bool Matches(string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;

            var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return terms.All(term =>
                Contains(Route, term) ||
                Contains(Description, term) ||
                Contains(MethodLabel, term) ||
                Contains(GroupName, term) ||
                Contains(Api?.Name, term) ||
                Parameters.Any(o => Contains(o.Name, term)));
        }

        private static bool Contains(string value, string term) => value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);


        /// <summary>
        /// The Route with the Route Parameters replaced by their values (left as "{name}" when no value is set)
        /// </summary>
        public string GetPath(IReadOnlyDictionary<string, string> values)
        {
            return _routeParameterRegex.Replace(Route ?? "/", match =>
            {
                var catchAll = match.Groups[1].Value.Length > 0;
                var name = match.Groups[2].Value.Trim();

                if (values != null && values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                {
                    // A Catch All Parameter can contain multiple Path Segments
                    if (catchAll) return string.Join("/", value.Split('/').Select(Uri.EscapeDataString));
                    return Uri.EscapeDataString(value);
                }

                return $"{{{name}}}";
            });
        }

        /// <summary>
        /// The Query String (including the "?") for the Query Parameters that have a value (empty if none do)
        /// </summary>
        public string GetQueryString(IReadOnlyDictionary<string, string> values, bool includePlaceholders = false)
        {
            var query = new StringBuilder();
            foreach (var parameter in QueryParameters)
            {
                string value = null;
                values?.TryGetValue(parameter.Name, out value);

                if (string.IsNullOrEmpty(value))
                {
                    if (!includePlaceholders || !parameter.Required) continue;
                    value = $"{{{parameter.Name}}}";
                    query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(parameter.Name)).Append('=').Append(value);
                    continue;
                }

                query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(parameter.Name)).Append('=').Append(Uri.EscapeDataString(value));
            }

            return query.ToString();
        }
    }

    public class ApiCatalogParameter
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// "Route", "Query", "Header", or "Body"
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; }

        [JsonPropertyName("defaultValue")]
        public string DefaultValue { get; set; }

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }


        public bool IsRoute => string.Equals(Type, "Route", StringComparison.OrdinalIgnoreCase);

        public bool IsQuery => string.Equals(Type, "Query", StringComparison.OrdinalIgnoreCase);

        public bool IsHeader => string.Equals(Type, "Header", StringComparison.OrdinalIgnoreCase);

        public bool IsBody => string.Equals(Type, "Body", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The Content Type the Body is sent as (Body Parameters only)
        /// </summary>
        public string BodyContentType
        {
            get
            {
                if (!string.IsNullOrEmpty(ContentType)) return ContentType;

                // Instances older than the Api Catalog don't send the Content Type
                switch (DataType)
                {
                    case "String": return "text/plain";
                    case "Byte[]":
                    case "Stream": return "application/octet-stream";
                    default: return "application/json";
                }
            }
        }
    }

    public class ApiCatalogResponse
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        [JsonPropertyName("schema")]
        public ApiCatalogSchema Schema { get; set; }
    }

    public class ApiCatalogSchema
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("properties")]
        public List<ApiCatalogSchemaProperty> Properties { get; set; }
    }

    public class ApiCatalogSchemaProperty
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("properties")]
        public List<ApiCatalogSchemaProperty> Properties { get; set; }
    }
}
