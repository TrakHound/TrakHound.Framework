// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// An API Stream that is open (or recently closed) on an Instance, from any Source (HTTP clients, Apps, Workers, Functions and other APIs)
    /// </summary>
    public class TrakHoundApiStreamInformation
    {
        [JsonPropertyName("streamId")]
        public string StreamId { get; set; }

        /// <summary>
        /// The ID the Api Provider gave the Request that opened the Stream (shown in the Endpoint's Log entries)
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

        /// <summary>
        /// The Route template of the Stream Endpoint (ex. {functionId}/runs/{runId}/status)
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The Route that was requested (ex. /functions/my-function/runs/123/status)
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// The Query Parameters (values of sensitive parameters, ex. token or apiKey, are replaced with ***)
        /// </summary>
        [JsonPropertyName("query")]
        public Dictionary<string, string> Query { get; set; }

        [JsonPropertyName("sourceType")]
        public TrakHoundApiRequestSourceType SourceType { get; set; }

        [JsonPropertyName("sourceId")]
        public string SourceId { get; set; }

        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        /// <summary>
        /// The User-Agent of the HTTP client that opened the Stream (null when it wasn't opened over HTTP)
        /// </summary>
        [JsonPropertyName("clientAgent")]
        public string ClientAgent { get; set; }

        [JsonPropertyName("opened")]
        public DateTime Opened { get; set; }

        /// <summary>
        /// When the Stream was closed (null while it is open)
        /// </summary>
        [JsonPropertyName("closed")]
        public DateTime? Closed { get; set; }

        /// <summary>
        /// Why the Stream was closed (see TrakHoundApiStreamCloseReasons)
        /// </summary>
        [JsonPropertyName("closeReason")]
        public string CloseReason { get; set; }

        /// <summary>
        /// The last error the Stream raised (ex. from a subscriber), if any
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }

        /// <summary>
        /// Messages the Stream delivered to its subscriber
        /// </summary>
        [JsonPropertyName("messageCount")]
        public long MessageCount { get; set; }

        /// <summary>
        /// Bytes of content in the delivered Messages
        /// </summary>
        [JsonPropertyName("byteCount")]
        public long ByteCount { get; set; }

        /// <summary>
        /// When the last Message was delivered (null if none were)
        /// </summary>
        [JsonPropertyName("lastMessage")]
        public DateTime? LastMessage { get; set; }


        [JsonIgnore]
        public bool IsOpen => Closed == null;

        /// <summary>
        /// How long the Stream has been (or was) open
        /// </summary>
        [JsonIgnore]
        public TimeSpan Duration => (Closed ?? DateTime.UtcNow) - Opened;

        /// <summary>
        /// How long since the last Message (or since the Stream opened when there were none)
        /// </summary>
        [JsonIgnore]
        public TimeSpan Idle => (Closed ?? DateTime.UtcNow) - (LastMessage ?? Opened);

        [JsonIgnore]
        public string Source => !string.IsNullOrEmpty(SourceName) ? $"{SourceType} : {SourceName}" : SourceType.ToString();


        public TrakHoundApiStreamInformation Clone() => (TrakHoundApiStreamInformation)MemberwiseClone();
    }

    /// <summary>
    /// The open and recently closed API Streams on an Instance
    /// </summary>
    public class TrakHoundApiStreams
    {
        /// <summary>
        /// The most closed Streams the Instance keeps (Settings > Metrics > Recent Requests). 0 when closed Streams are not kept.
        /// </summary>
        [JsonPropertyName("closedCapacity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ClosedCapacity { get; set; }

        /// <summary>
        /// The open Streams, newest first
        /// </summary>
        [JsonPropertyName("open")]
        public IEnumerable<TrakHoundApiStreamInformation> Open { get; set; }

        /// <summary>
        /// The most recently closed Streams, most recently closed first
        /// </summary>
        [JsonPropertyName("closed")]
        public IEnumerable<TrakHoundApiStreamInformation> Closed { get; set; }
    }

    /// <summary>
    /// Why an API Stream was closed
    /// </summary>
    public static class TrakHoundApiStreamCloseReasons
    {
        /// <summary>
        /// The Source disposed the Stream (ex. the HTTP client disconnected, or the App, Worker or Function stopped subscribing)
        /// </summary>
        public const string ClosedBySource = "Closed by Source";

        /// <summary>
        /// Closed from the Admin Interface or the System API
        /// </summary>
        public const string ClosedByAdmin = "Closed by Admin";

        /// <summary>
        /// The Stream raised an error before it closed
        /// </summary>
        public const string Error = "Error";

        /// <summary>
        /// The Api Provider was stopped (ex. the Instance stopped)
        /// </summary>
        public const string Stopped = "Stopped";
    }
}
