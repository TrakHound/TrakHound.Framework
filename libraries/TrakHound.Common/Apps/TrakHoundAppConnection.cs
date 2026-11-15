// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// A Client Connection (a Browser tab) to the Instance's App Pages and the Page it is currently viewing
    /// </summary>
    public class TrakHoundAppConnection
    {
        /// <summary>
        /// The HTTP Connection ID of the Client Connection
        /// </summary>
        [JsonPropertyName("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>
        /// A short code for the connection (shown on the page by Identify, to match a screen to its connection)
        /// </summary>
        [JsonPropertyName("code")]
        public string Code { get; set; }

        /// <summary>
        /// The IP Address of the Client
        /// </summary>
        [JsonPropertyName("clientAddress")]
        public string ClientAddress { get; set; }

        /// <summary>
        /// The Hostname of the Client (resolved by Reverse DNS in the background, so it may be null)
        /// </summary>
        [JsonPropertyName("clientHostname")]
        public string ClientHostname { get; set; }

        /// <summary>
        /// The User Agent of the Client's Browser
        /// </summary>
        [JsonPropertyName("clientAgent")]
        public string ClientAgent { get; set; }

        /// <summary>
        /// A display Name for what the Connection is viewing (ex. the App Package ID, or "Admin" for Instance pages)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The relative Path of the current Page
        /// </summary>
        [JsonPropertyName("requestedPath")]
        public string RequestedPath { get; set; }

        /// <summary>
        /// The App Configuration ID of the current Page
        /// </summary>
        [JsonPropertyName("appId")]
        public string AppId { get; set; }

        /// <summary>
        /// The App Package Version the connection is running (may be older than the App's current Version after an update)
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Hash of the App Package the connection is running
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// When the Connection was created (UTC)
        /// </summary>
        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        /// <summary>
        /// When the Connection last navigated to a Page (UTC)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// When the user last interacted with the page (mouse, keyboard, touch or scroll) in UTC, as reported by the Browser.
        /// Null until the page has reported its activity
        /// </summary>
        [JsonPropertyName("lastActivity")]
        public DateTime? LastActivity { get; set; }

        /// <summary>
        /// A text field (or other editable element) on the page has focus
        /// </summary>
        [JsonPropertyName("isEditing")]
        public bool IsEditing { get; set; }

        /// <summary>
        /// Input has been typed or changed on the current page (it may or may not have been saved)
        /// </summary>
        [JsonPropertyName("hasInput")]
        public bool HasInput { get; set; }

        /// <summary>
        /// Whether the current page has unsaved changes, as declared by the App (null when the App doesn't declare it)
        /// </summary>
        [JsonPropertyName("hasUnsavedChanges")]
        public bool? HasUnsavedChanges { get; set; }

        /// <summary>
        /// The page is in a Background (hidden) Browser tab or a minimized window
        /// </summary>
        [JsonPropertyName("isHidden")]
        public bool IsHidden { get; set; }
    }
}
