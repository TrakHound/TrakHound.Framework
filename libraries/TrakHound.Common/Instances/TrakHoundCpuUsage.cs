// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// The CPU used by an Instance process, by Module Type (Workers, Functions, APIs, Apps and the Admin Interface) and by Module.
    /// Usage values are percent of total machine capacity (0 - 100), averaged over the last second. CPU Times are in milliseconds since the Instance started.
    /// </summary>
    public class TrakHoundCpuUsage
    {
        /// <summary>
        /// Whether CPU usage is measured by Module (Settings > Metrics > Measure CPU Usage). When false only the Process usage is set.
        /// </summary>
        [JsonPropertyName("enabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool Enabled { get; set; }

        /// <summary>
        /// Whether per-thread CPU time is supported on the platform (when it isn't, the time a Module holds a thread is measured instead)
        /// </summary>
        [JsonPropertyName("threadTimeSupported")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool ThreadTimeSupported { get; set; }

        [JsonPropertyName("processorCount")]
        public int ProcessorCount { get; set; }

        /// <summary>
        /// The CPU used by the whole Instance process
        /// </summary>
        [JsonPropertyName("processUsage")]
        public double ProcessUsage { get; set; }

        /// <summary>
        /// The Process usage not measured for any Module (ex. the HTTP server, Services' own background work, and the .NET Garbage Collector)
        /// </summary>
        [JsonPropertyName("unattributedUsage")]
        public double UnattributedUsage { get; set; }

        /// <summary>
        /// The usage of each Module Type (see TrakHoundCpuModuleTypes)
        /// </summary>
        [JsonPropertyName("types")]
        public IEnumerable<TrakHoundCpuTypeUsage> Types { get; set; }

        /// <summary>
        /// The usage of each Module that has been measured since the Instance started
        /// </summary>
        [JsonPropertyName("modules")]
        public IEnumerable<TrakHoundCpuModuleUsage> Modules { get; set; }

        /// <summary>
        /// The usage of the Process and each Module Type every second (oldest first)
        /// </summary>
        [JsonPropertyName("history")]
        public IEnumerable<TrakHoundCpuUsageSample> History { get; set; }
    }

    public class TrakHoundCpuTypeUsage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("usage")]
        public double Usage { get; set; }

        /// <summary>
        /// Milliseconds of CPU time since the Instance started
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }
    }

    public class TrakHoundCpuModuleUsage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The ID of the Worker, Function, API or App
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("usage")]
        public double Usage { get; set; }

        /// <summary>
        /// Milliseconds of CPU time since the Instance started
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }
    }

    public class TrakHoundCpuUsageSample
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("process")]
        public double Process { get; set; }

        /// <summary>
        /// The usage of each Module Type (by TrakHoundCpuModuleTypes value)
        /// </summary>
        [JsonPropertyName("types")]
        public Dictionary<string, double> Types { get; set; }

        [JsonPropertyName("unattributed")]
        public double Unattributed { get; set; }
    }

    /// <summary>
    /// The Module Types CPU usage is measured for
    /// </summary>
    public static class TrakHoundCpuModuleTypes
    {
        public const string Worker = "Worker";
        public const string Function = "Function";
        public const string Api = "API";
        public const string App = "App";

        /// <summary>
        /// The Admin Interface (Blazor Circuits that aren't showing an App)
        /// </summary>
        public const string Admin = "Admin";

        public static readonly IReadOnlyList<string> All = new[] { Worker, Function, Api, App, Admin };
    }
}
