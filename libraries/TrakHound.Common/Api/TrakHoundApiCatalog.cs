// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// The Apis and Endpoints that the caller can use (for developers building their own applications). Only includes Apis served over
    /// HTTP and the Endpoints that the caller's Identity is allowed to call. Internal details (Volumes, Package Hashes, Metrics) are not included
    /// </summary>
    public class TrakHoundApiCatalog
    {
        /// <summary>
        /// The Identity the Catalog was created for (null for an anonymous Request)
        /// </summary>
        [JsonPropertyName("identity")]
        public TrakHoundApiCatalogIdentity Identity { get; set; }

        /// <summary>
        /// The Identity Providers that can be signed in to interactively (ex. to see Endpoints that require Authentication)
        /// </summary>
        [JsonPropertyName("signInProviders")]
        public IEnumerable<TrakHoundApiCatalogSignInProvider> SignInProviders { get; set; }

        /// <summary>
        /// The Header that API Keys issued by the Instance are sent in (they can also be sent as a Bearer Token)
        /// </summary>
        [JsonPropertyName("apiKeyHeader")]
        public string ApiKeyHeader { get; set; }

        /// <summary>
        /// The Apis with at least one Endpoint that the caller can use
        /// </summary>
        [JsonPropertyName("apis")]
        public IEnumerable<TrakHoundApiCatalogApi> Apis { get; set; }
    }

    /// <summary>
    /// The Identity a Catalog was created for
    /// </summary>
    public class TrakHoundApiCatalogIdentity
    {
        [JsonPropertyName("providerId")]
        public string ProviderId { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// An Identity Provider that can be signed in to from a browser
    /// </summary>
    public class TrakHoundApiCatalogSignInProvider
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The path to sign in at (relative to the Instance's Base Path). Add a "returnUrl" Query Parameter to return to a page afterwards
        /// </summary>
        [JsonPropertyName("signInPath")]
        public string SignInPath { get; set; }
    }

    /// <summary>
    /// An Api in a Catalog
    /// </summary>
    public class TrakHoundApiCatalogApi
    {
        /// <summary>
        /// The ID of the Api Configuration
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Route the Api is served at (relative to the Instance's Base Path, always starts with "/")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The Version of the Api's Package
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The Endpoints that the caller can use
        /// </summary>
        [JsonPropertyName("endpoints")]
        public IEnumerable<TrakHoundApiCatalogEndpoint> Endpoints { get; set; }
    }

    /// <summary>
    /// An Endpoint in a Catalog
    /// </summary>
    public class TrakHoundApiCatalogEndpoint
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("controllerId")]
        public string ControllerId { get; set; }

        /// <summary>
        /// The name of the Controller the Endpoint belongs to
        /// </summary>
        [JsonPropertyName("controller")]
        public string Controller { get; set; }

        [JsonPropertyName("controllerDescription")]
        public string ControllerDescription { get; set; }

        [JsonPropertyName("group")]
        public string Group { get; set; }

        /// <summary>
        /// "Request" (HTTP) or "Stream" (WebSocket)
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundApiEndpointType Type { get; set; }

        /// <summary>
        /// The HTTP Method (null for Stream Endpoints)
        /// </summary>
        [JsonPropertyName("method")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TrakHoundApiMethod? Method { get; set; }

        /// <summary>
        /// The full Route of the Endpoint (the Api, Controller, and Endpoint Routes combined, relative to the Instance's Base Path)
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public IEnumerable<TrakHoundApiParameterInformation> Parameters { get; set; }

        [JsonPropertyName("responses")]
        public IEnumerable<TrakHoundApiResponseInformation> Responses { get; set; }

        /// <summary>
        /// True if the Endpoint can only be called with Credentials (a Session, API Key, or Bearer Token)
        /// </summary>
        [JsonPropertyName("authenticationRequired")]
        public bool AuthenticationRequired { get; set; }
    }
}
