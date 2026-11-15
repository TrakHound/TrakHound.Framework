// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// Page Views and Durations for an App, totaled across Client Connections since the Instance started.
    /// Durations are in milliseconds and include the time of views that are still open.
    /// </summary>
    public class TrakHoundAppMetrics
    {
        /// <summary>
        /// The ID of the App (App Configuration)
        /// </summary>
        [JsonPropertyName("appId")]
        public string AppId { get; set; }

        /// <summary>
        /// The Name of the App (from the most recently viewed Page)
        /// </summary>
        [JsonPropertyName("appName")]
        public string AppName { get; set; }

        /// <summary>
        /// The ID of the App's Package
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The total number of Page Views across all of the App's Pages
        /// </summary>
        [JsonPropertyName("totalPageViews")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalPageViews { get; set; }

        /// <summary>
        /// Page Views that are currently open
        /// </summary>
        [JsonPropertyName("activePageViews")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ActivePageViews { get; set; }

        /// <summary>
        /// Client Connections that viewed a Page of the App
        /// </summary>
        [JsonPropertyName("totalConnections")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalConnections { get; set; }

        /// <summary>
        /// Total time spent on the App's Pages (milliseconds)
        /// </summary>
        [JsonPropertyName("totalPageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalPageDuration { get; set; }

        /// <summary>
        /// Average time per Page View (milliseconds, 0 if there are no Page Views)
        /// </summary>
        [JsonPropertyName("averagePageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AveragePageDuration { get; set; }

        /// <summary>
        /// The longest single Page View (milliseconds)
        /// </summary>
        [JsonPropertyName("maxPageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxPageDuration { get; set; }

        /// <summary>
        /// When a Page of the App was last viewed (UTC)
        /// </summary>
        [JsonPropertyName("lastViewed")]
        public DateTime? LastViewed { get; set; }

        /// <summary>
        /// The CPU used by the App's Pages in their Blazor Circuits (percent of total machine capacity, 0 - 100) over the last second. 0 when CPU usage isn't measured (Settings > Metrics > Measure CPU Usage).
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        public double CpuUsage { get; set; }

        /// <summary>
        /// Milliseconds of CPU time used by the App's Pages since the Instance started
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }

        /// <summary>
        /// The Metrics for each Page of the App that has been viewed
        /// </summary>
        [JsonPropertyName("pages")]
        public IEnumerable<TrakHoundAppPageMetrics> Pages { get; set; }
    }
}
