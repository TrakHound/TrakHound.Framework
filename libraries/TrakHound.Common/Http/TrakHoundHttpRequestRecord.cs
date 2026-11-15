// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Http
{
    /// <summary>
    /// A completed HTTP Request received by an Instance (kept in the Instance's Recent Requests)
    /// </summary>
    public class TrakHoundHttpRequestRecord
    {
        /// <summary>
        /// Identifies the Request within the Instance (increases with each Request)
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>
        /// The address of the Client that sent the Request
        /// </summary>
        [JsonPropertyName("client")]
        public string Client { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        /// <summary>
        /// The Path and Query String (values of sensitive Query parameters, ex. token or apiKey, are replaced with ***)
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// The Route template of the matched Endpoint (ex. /_api/{id}), the same Route the HTTP Metrics group Endpoints by
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// What the Request was for (System API, API, Apps, Admin, Blazor, Static Files, WebSockets, Event Streams, MCP, Other)
        /// </summary>
        [JsonPropertyName("area")]
        public string Area { get; set; }

        /// <summary>
        /// WebSocket or Event Stream when the Request stayed open for the life of a connection (its Duration is how long the connection was open, not a response time)
        /// </summary>
        [JsonPropertyName("stream")]
        public string Stream { get; set; }

        /// <summary>
        /// Bytes received in the Request body
        /// </summary>
        [JsonPropertyName("requestBytes")]
        public long RequestBytes { get; set; }

        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Bytes sent in the Response body
        /// </summary>
        [JsonPropertyName("responseBytes")]
        public long ResponseBytes { get; set; }

        [JsonPropertyName("requested")]
        public DateTime Requested { get; set; }

        [JsonPropertyName("completed")]
        public DateTime Completed { get; set; }


        [JsonIgnore]
        public TimeSpan Duration => Completed - Requested;

        [JsonIgnore]
        public bool IsStream => !string.IsNullOrEmpty(Stream);
    }

    /// <summary>
    /// What a long-lived HTTP Request was (TrakHoundHttpRequestRecord.Stream)
    /// </summary>
    public static class TrakHoundHttpRequestStreams
    {
        public const string WebSocket = "WebSocket";
        public const string EventStream = "Event Stream";
    }
}
