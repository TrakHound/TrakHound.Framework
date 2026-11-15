// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// App Page Views and Durations for a single Client Connection (a browser tab connected to the Instance).
    /// Durations are in milliseconds and include the time of the Page that is currently open.
    /// </summary>
    public class TrakHoundAppConnectionMetrics
    {
        /// <summary>
        /// Unique ID of the Client Connection
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The HTTP Connection ID (used to Refresh the Connection)
        /// </summary>
        [JsonPropertyName("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>
        /// The Connection's short Code (shown on the page by Identify, and in the Access Log)
        /// </summary>
        [JsonPropertyName("code")]
        public string Code { get; set; }

        /// <summary>
        /// The IP Address of the Client
        /// </summary>
        [JsonPropertyName("clientAddress")]
        public string ClientAddress { get; set; }

        /// <summary>
        /// The Hostname of the Client (null if it could not be resolved)
        /// </summary>
        [JsonPropertyName("clientHostname")]
        public string ClientHostname { get; set; }

        /// <summary>
        /// The User Agent of the Client's Browser
        /// </summary>
        [JsonPropertyName("clientAgent")]
        public string ClientAgent { get; set; }

        /// <summary>
        /// When the Connection was opened (UTC)
        /// </summary>
        [JsonPropertyName("connected")]
        public DateTime Connected { get; set; }

        /// <summary>
        /// When the Connection closed (null while it is still connected)
        /// </summary>
        [JsonPropertyName("disconnected")]
        public DateTime? Disconnected { get; set; }

        /// <summary>
        /// True while the Connection is still open (Disconnected is null)
        /// </summary>
        [JsonPropertyName("isConnected")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsConnected { get; set; }

        /// <summary>
        /// The number of App Pages viewed by this Connection
        /// </summary>
        [JsonPropertyName("totalPageViews")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalPageViews { get; set; }

        /// <summary>
        /// Time spent on App Pages (milliseconds)
        /// </summary>
        [JsonPropertyName("totalPageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalPageDuration { get; set; }

        /// <summary>
        /// The ID of the App of the current Page (null if no App Page is open)
        /// </summary>
        [JsonPropertyName("currentAppId")]
        public string CurrentAppId { get; set; }

        /// <summary>
        /// The Name of the App of the current Page (null if no App Page is open)
        /// </summary>
        [JsonPropertyName("currentAppName")]
        public string CurrentAppName { get; set; }

        /// <summary>
        /// The Route Template of the current Page (ex. "/orders/{id}")
        /// </summary>
        [JsonPropertyName("currentRoute")]
        public string CurrentRoute { get; set; }

        /// <summary>
        /// The Path of the current Page (ex. "/orders/1234")
        /// </summary>
        [JsonPropertyName("currentPath")]
        public string CurrentPath { get; set; }

        /// <summary>
        /// Time on the current Page (milliseconds)
        /// </summary>
        [JsonPropertyName("currentPageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CurrentPageDuration { get; set; }

        /// <summary>
        /// The Pages viewed by this Connection
        /// </summary>
        [JsonPropertyName("pages")]
        public IEnumerable<TrakHoundAppPageMetrics> Pages { get; set; }
    }
}
