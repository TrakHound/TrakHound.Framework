// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// Views and Durations of an App Page (identified by its Route Template, ex. "/orders/{id}").
    /// Durations are in milliseconds and include the time of views that are still open.
    /// </summary>
    public class TrakHoundAppPageMetrics
    {
        /// <summary>
        /// The ID of the App (App Configuration) the Page belongs to
        /// </summary>
        [JsonPropertyName("appId")]
        public string AppId { get; set; }

        /// <summary>
        /// The Name of the App the Page belongs to
        /// </summary>
        [JsonPropertyName("appName")]
        public string AppName { get; set; }

        /// <summary>
        /// The Route Template of the Page (ex. "/orders/{id}")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The most recently viewed Path (ex. "/orders/1234")
        /// </summary>
        [JsonPropertyName("lastPath")]
        public string LastPath { get; set; }

        /// <summary>
        /// The total number of times the Page has been viewed
        /// </summary>
        [JsonPropertyName("totalViews")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalViews { get; set; }

        /// <summary>
        /// Views that are currently open
        /// </summary>
        [JsonPropertyName("activeViews")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ActiveViews { get; set; }

        /// <summary>
        /// Total time spent on the Page (milliseconds)
        /// </summary>
        [JsonPropertyName("totalDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalDuration { get; set; }

        /// <summary>
        /// Average time per View (milliseconds, 0 if there are no Views)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// The longest single View (milliseconds)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// When the Page was last viewed (UTC)
        /// </summary>
        [JsonPropertyName("lastViewed")]
        public DateTime? LastViewed { get; set; }
    }
}
