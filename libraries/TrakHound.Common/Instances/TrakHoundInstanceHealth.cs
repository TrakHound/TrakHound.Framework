// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// The Health of an Instance (in order of severity). Unknown checks are not included in the overall Status.
    /// </summary>
    public enum TrakHoundHealthStatus
    {
        /// <summary>
        /// The Health could not be determined
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Everything is operating normally
        /// </summary>
        Healthy = 1,

        /// <summary>
        /// Operating, but with problems that should be looked at
        /// </summary>
        Degraded = 2,

        /// <summary>
        /// Not operating correctly
        /// </summary>
        Unhealthy = 3
    }

    /// <summary>
    /// The overall Health of an Instance and the result of each Health Check
    /// </summary>
    public class TrakHoundInstanceHealth
    {
        /// <summary>
        /// The Identifier of the Instance
        /// </summary>
        [JsonPropertyName("instanceId")]
        public string InstanceId { get; set; }

        /// <summary>
        /// The most severe Status of the Checks
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundHealthStatus Status { get; set; }

        /// <summary>
        /// When the overall Status last changed
        /// </summary>
        [JsonPropertyName("statusChanged")]
        public DateTime? StatusChanged { get; set; }

        /// <summary>
        /// When the Checks were last evaluated
        /// </summary>
        [JsonPropertyName("checked")]
        public DateTime Checked { get; set; }

        /// <summary>
        /// The result of each Health Check
        /// </summary>
        [JsonPropertyName("checks")]
        public IEnumerable<TrakHoundHealthCheckResult> Checks { get; set; }
    }

    /// <summary>
    /// The result of a single Health Check (ex. Workers)
    /// </summary>
    public class TrakHoundHealthCheckResult
    {
        /// <summary>
        /// The Identifier of the Check (ex. workers)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The display Title of the Check (ex. Workers)
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// The Status of the Check
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundHealthStatus Status { get; set; }

        /// <summary>
        /// A short summary (ex. 1 of 4 Workers in Error)
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// When the Status last changed
        /// </summary>
        [JsonPropertyName("statusChanged")]
        public DateTime? StatusChanged { get; set; }

        /// <summary>
        /// The Explorer page with more detail (relative to the Instance in the Explorer, ex. diagnostics/workers)
        /// </summary>
        [JsonPropertyName("link")]
        public string Link { get; set; }

        /// <summary>
        /// The Check is Ignored by the Health settings (its Status is not included in the overall Status)
        /// </summary>
        [JsonPropertyName("ignored")]
        public bool Ignored { get; set; }

        /// <summary>
        /// The Check is Critical by the Health settings (Degraded is reported as Unhealthy)
        /// </summary>
        [JsonPropertyName("critical")]
        public bool Critical { get; set; }

        /// <summary>
        /// The items that are not Healthy (and any worth showing for context)
        /// </summary>
        [JsonPropertyName("items")]
        public IEnumerable<TrakHoundHealthCheckItem> Items { get; set; }
    }

    /// <summary>
    /// A single item within a Health Check (ex. a Worker)
    /// </summary>
    public class TrakHoundHealthCheckItem
    {
        /// <summary>
        /// The Identifier of the item (ex. a Worker Id)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the item
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Status of the item
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundHealthStatus Status { get; set; }

        /// <summary>
        /// A short description of the item's Status (ex. the reason it is not Healthy)
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Explorer page with more detail (relative to the Instance in the Explorer)
        /// </summary>
        [JsonPropertyName("link")]
        public string Link { get; set; }

        /// <summary>
        /// Identifies the item in the Health settings (Ignore and Critical lists), ex. workers/TrakHound.Example.Worker
        /// </summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        /// <summary>
        /// The item is Ignored by the Health settings (its Status is not included in the Check)
        /// </summary>
        [JsonPropertyName("ignored")]
        public bool Ignored { get; set; }

        /// <summary>
        /// The item is Critical by the Health settings (Degraded is reported as Unhealthy)
        /// </summary>
        [JsonPropertyName("critical")]
        public bool Critical { get; set; }
    }
}
