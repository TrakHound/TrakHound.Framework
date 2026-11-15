// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Http
{
    /// <summary>
    /// Metrics for the HTTP Stream connections (WebSockets and Server-Sent Events) of an Instance (API, Worker, Function, Log, Message and Message Queue streams).
    /// Rates are calculated over a recent window, Totals are since the Instance started
    /// </summary>
    public class TrakHoundStreamMetrics
    {
        /// <summary>
        /// The window (in seconds) that the Rates are calculated over
        /// </summary>
        [JsonPropertyName("window")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Window { get; set; }

        /// <summary>
        /// Connections that are currently open
        /// </summary>
        [JsonPropertyName("openConnectionCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int OpenConnectionCount { get; set; }

        /// <summary>
        /// The most connections open at the same time since the Instance started
        /// </summary>
        [JsonPropertyName("peakConnectionCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int PeakConnectionCount { get; set; }

        /// <summary>
        /// Connections accepted since the Instance started
        /// </summary>
        [JsonPropertyName("totalConnectionCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalConnectionCount { get; set; }

        /// <summary>
        /// WebSocket and Event Stream requests that were not accepted (ex. the Stream was not found)
        /// </summary>
        [JsonPropertyName("totalRejectedCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRejectedCount { get; set; }

        /// <summary>
        /// Messages sent to Clients since the Instance started
        /// </summary>
        [JsonPropertyName("totalMessagesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalMessagesSent { get; set; }

        /// <summary>
        /// Messages received from Clients since the Instance started
        /// </summary>
        [JsonPropertyName("totalMessagesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalMessagesReceived { get; set; }

        /// <summary>
        /// Bytes sent to Clients since the Instance started
        /// </summary>
        [JsonPropertyName("totalBytesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesSent { get; set; }

        /// <summary>
        /// Bytes received from Clients since the Instance started
        /// </summary>
        [JsonPropertyName("totalBytesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesReceived { get; set; }

        /// <summary>
        /// Messages that failed to send, and messages received that could not be read
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Connections opened per second
        /// </summary>
        [JsonPropertyName("connectionRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ConnectionRate { get; set; }

        /// <summary>
        /// Connections closed per second
        /// </summary>
        [JsonPropertyName("disconnectionRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double DisconnectionRate { get; set; }

        /// <summary>
        /// Messages sent per second
        /// </summary>
        [JsonPropertyName("messageSentRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MessageSentRate { get; set; }

        /// <summary>
        /// Messages received per second
        /// </summary>
        [JsonPropertyName("messageReceivedRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MessageReceivedRate { get; set; }

        /// <summary>
        /// Bytes sent per second
        /// </summary>
        [JsonPropertyName("byteSentRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ByteSentRate { get; set; }

        /// <summary>
        /// Bytes received per second
        /// </summary>
        [JsonPropertyName("byteReceivedRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ByteReceivedRate { get; set; }

        /// <summary>
        /// Errors per second
        /// </summary>
        [JsonPropertyName("errorRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ErrorRate { get; set; }

        /// <summary>
        /// Average duration (milliseconds) of the connections that have closed
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Longest duration (milliseconds) of the connections that have closed
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The time (UTC) the most recent connection was opened (null if none have been opened)
        /// </summary>
        [JsonPropertyName("lastConnected")]
        public DateTime? LastConnected { get; set; }

        /// <summary>
        /// How the closed connections ended (Closed by Client, Disconnected, Error, Message Too Big, Shutdown)
        /// </summary>
        [JsonPropertyName("closeReasons")]
        public IEnumerable<TrakHoundStreamCloseReasonMetrics> CloseReasons { get; set; }

        /// <summary>
        /// Metrics for each Stream Endpoint (Route)
        /// </summary>
        [JsonPropertyName("endpoints")]
        public IEnumerable<TrakHoundStreamEndpointMetrics> Endpoints { get; set; }

        /// <summary>
        /// The connections that are currently open
        /// </summary>
        [JsonPropertyName("connections")]
        public IEnumerable<TrakHoundStreamConnectionMetrics> Connections { get; set; }
    }

    /// <summary>
    /// The number of connections that closed for a Reason since the Instance started
    /// </summary>
    public class TrakHoundStreamCloseReasonMetrics
    {
        /// <summary>
        /// Why the connections closed (Closed by Client, Disconnected, Error, Message Too Big, Shutdown)
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        /// <summary>
        /// The number of connections that closed for the Reason
        /// </summary>
        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long Count { get; set; }
    }

    /// <summary>
    /// Metrics for a Stream Endpoint (Route) since the Instance started
    /// </summary>
    public class TrakHoundStreamEndpointMetrics
    {
        /// <summary>
        /// How the Stream is sent to the client (WebSocket or SSE for Server-Sent Events)
        /// </summary>
        [JsonPropertyName("transport")]
        public string Transport { get; set; }

        /// <summary>
        /// Api, Workers, Functions, Logs, Messages or MessageQueues
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The Route template of the Endpoint
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// Connections to the Endpoint that are currently open
        /// </summary>
        [JsonPropertyName("openConnectionCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int OpenConnectionCount { get; set; }

        /// <summary>
        /// Connections accepted by the Endpoint
        /// </summary>
        [JsonPropertyName("totalConnectionCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalConnectionCount { get; set; }

        /// <summary>
        /// Messages sent by the Endpoint
        /// </summary>
        [JsonPropertyName("totalMessagesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalMessagesSent { get; set; }

        /// <summary>
        /// Messages received by the Endpoint
        /// </summary>
        [JsonPropertyName("totalMessagesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalMessagesReceived { get; set; }

        /// <summary>
        /// Bytes sent by the Endpoint
        /// </summary>
        [JsonPropertyName("totalBytesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesSent { get; set; }

        /// <summary>
        /// Bytes received by the Endpoint
        /// </summary>
        [JsonPropertyName("totalBytesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesReceived { get; set; }

        /// <summary>
        /// Messages that failed to send, and messages received that could not be read
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// The time (UTC) the most recent connection to the Endpoint was opened
        /// </summary>
        [JsonPropertyName("lastConnected")]
        public DateTime? LastConnected { get; set; }
    }

    /// <summary>
    /// Metrics for an open Stream connection (WebSocket or Server-Sent Events)
    /// </summary>
    public class TrakHoundStreamConnectionMetrics
    {
        /// <summary>
        /// A unique Identifier for the connection (generated when it was opened)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// How the Stream is sent to the client (WebSocket or SSE for Server-Sent Events)
        /// </summary>
        [JsonPropertyName("transport")]
        public string Transport { get; set; }

        /// <summary>
        /// Api, Workers, Functions, Logs, Messages or MessageQueues
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The Route template of the Endpoint the connection is for
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The requested Path of the connection
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// The Address of the remote Client
        /// </summary>
        [JsonPropertyName("remoteAddress")]
        public string RemoteAddress { get; set; }

        /// <summary>
        /// The time (UTC) the connection was opened
        /// </summary>
        [JsonPropertyName("connected")]
        public DateTime Connected { get; set; }

        /// <summary>
        /// Messages sent on the connection
        /// </summary>
        [JsonPropertyName("messagesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long MessagesSent { get; set; }

        /// <summary>
        /// Messages received on the connection
        /// </summary>
        [JsonPropertyName("messagesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long MessagesReceived { get; set; }

        /// <summary>
        /// Bytes sent on the connection
        /// </summary>
        [JsonPropertyName("bytesSent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long BytesSent { get; set; }

        /// <summary>
        /// Bytes received on the connection
        /// </summary>
        [JsonPropertyName("bytesReceived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long BytesReceived { get; set; }

        /// <summary>
        /// Messages on the connection that failed to send or could not be read
        /// </summary>
        [JsonPropertyName("errorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long ErrorCount { get; set; }

        /// <summary>
        /// The last time a message was sent or received
        /// </summary>
        [JsonPropertyName("lastActivity")]
        public DateTime? LastActivity { get; set; }
    }
}
