// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// A completed API Request (kept in the Instance's Recent API Requests)
    /// </summary>
    public class TrakHoundApiRequestRecord
    {
        /// <summary>
        /// Identifies the Request within the Instance (increases with each Request)
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>
        /// The ID the Api Provider gave the Request (shown in the Endpoint's Log entries for the Request)
        /// </summary>
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("apiId")]
        public string ApiId { get; set; }

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The ID of the Controller Information the Endpoint belongs to (null if the Endpoint isn't described)
        /// </summary>
        [JsonPropertyName("controllerId")]
        public string ControllerId { get; set; }

        /// <summary>
        /// The ID of the Endpoint Information (null if the Endpoint isn't described)
        /// </summary>
        [JsonPropertyName("endpointId")]
        public string EndpointId { get; set; }

        [JsonPropertyName("endpointType")]
        public TrakHoundApiEndpointType EndpointType { get; set; }

        [JsonPropertyName("method")]
        public TrakHoundApiMethod Method { get; set; }

        /// <summary>
        /// The Route template of the Endpoint that handled the Request (ex. runs/{id}), the same Route the Api Metrics group Endpoints by
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The Route that was requested (ex. /functions/runs/123)
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// The Query Parameters (values of sensitive parameters, ex. token or apiKey, are replaced with ***). The parameters the Instance adds (ex. _client_address) aren't included.
        /// </summary>
        [JsonPropertyName("query")]
        public Dictionary<string, string> Query { get; set; }

        /// <summary>
        /// The User-Agent of the HTTP client that sent the Request (null when it wasn't an HTTP Request)
        /// </summary>
        [JsonPropertyName("clientAgent")]
        public string ClientAgent { get; set; }

        [JsonPropertyName("sourceType")]
        public TrakHoundApiRequestSourceType SourceType { get; set; }

        [JsonPropertyName("sourceId")]
        public string SourceId { get; set; }

        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        /// <summary>
        /// Bytes received in the Request body
        /// </summary>
        [JsonPropertyName("requestBytes")]
        public long RequestBytes { get; set; }

        /// <summary>
        /// The Status Code (0 for a Stream, which has no Status Code)
        /// </summary>
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Bytes sent in the Response body
        /// </summary>
        [JsonPropertyName("responseBytes")]
        public long ResponseBytes { get; set; }

        /// <summary>
        /// The Response content when the Status Code is 400 or higher (ex. the Exception message for a 500), up to 1,000 characters
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("requested")]
        public DateTime Requested { get; set; }

        /// <summary>
        /// When the Response was returned (for a Stream, when the Stream was opened)
        /// </summary>
        [JsonPropertyName("completed")]
        public DateTime Completed { get; set; }


        [JsonIgnore]
        public TimeSpan Duration => Completed - Requested;

        [JsonIgnore]
        public bool IsStream => EndpointType == TrakHoundApiEndpointType.Stream;

        [JsonIgnore]
        public string Source => !string.IsNullOrEmpty(SourceName) ? $"{SourceType} : {SourceName}" : SourceType.ToString();
    }

    /// <summary>
    /// The most recent API Requests received by an Instance
    /// </summary>
    public class TrakHoundApiRecentRequests
    {
        /// <summary>
        /// The most Requests the Instance keeps (Settings > Metrics > Recent Requests). 0 when Recent Requests are not kept.
        /// </summary>
        [JsonPropertyName("capacity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Capacity { get; set; }

        /// <summary>
        /// The Requests, newest first
        /// </summary>
        [JsonPropertyName("requests")]
        public IEnumerable<TrakHoundApiRequestRecord> Requests { get; set; }
    }
}
