// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// How a Boot (one run of the Instance process) ended, or that it is still running
    /// </summary>
    public enum TrakHoundInstanceBootStatus
    {
        /// <summary>
        /// The process is Starting (the HTTP Server and the Runtime are not running yet)
        /// </summary>
        Starting = 0,

        /// <summary>
        /// The process Started and is still running
        /// </summary>
        Running = 1,

        /// <summary>
        /// The process Shut Down normally
        /// </summary>
        Stopped = 2,

        /// <summary>
        /// The process could not Start (ex. the HTTP port is in use)
        /// </summary>
        Failed = 3,

        /// <summary>
        /// The process Crashed with an Unhandled Exception
        /// </summary>
        Crashed = 4,

        /// <summary>
        /// The process ended without Shutting Down (ex. Killed, Power Loss or a Stack Overflow). Found when the next process Starts
        /// </summary>
        Unclean = 5
    }

    /// <summary>
    /// One run of the Instance process : when it Started, how long each step took, the Runtime Starts within it, and how it ended
    /// </summary>
    public class TrakHoundInstanceBoot
    {
        /// <summary>
        /// The Identifier of the Boot (the process Start time and Process ID)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundInstanceBootStatus Status { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; }

        [JsonPropertyName("user")]
        public string User { get; set; }

        [JsonPropertyName("operatingSystem")]
        public string OperatingSystem { get; set; }

        [JsonPropertyName("runtime")]
        public string Runtime { get; set; }

        /// <summary>
        /// When the process Started (UTC)
        /// </summary>
        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        /// <summary>
        /// When the HTTP Server and the first Runtime Start finished (UTC). Null if the process never got that far
        /// </summary>
        [JsonPropertyName("ready")]
        public DateTime? Ready { get; set; }

        /// <summary>
        /// When the process ended (UTC). For an Unclean Boot, the last time the process was known to be running
        /// </summary>
        [JsonPropertyName("ended")]
        public DateTime? Ended { get; set; }

        /// <summary>
        /// Why the process ended (ex. a Windows Service Stop, a Signal, or the Exception that Crashed it)
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        /// <summary>
        /// The full Exception (with Stack Trace) if the process Failed to Start or Crashed
        /// </summary>
        [JsonPropertyName("details")]
        public string Details { get; set; }

        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        /// <summary>
        /// The steps of Starting the process, before the Runtime Starts (ex. Initialize, Start HTTP Server)
        /// </summary>
        [JsonPropertyName("steps")]
        public List<TrakHoundInstanceBootStep> Steps { get; set; }

        /// <summary>
        /// Warning and Error Log entries between the process Starting and Ready
        /// </summary>
        [JsonPropertyName("log")]
        public TrakHoundInstanceBootLog Log { get; set; }

        /// <summary>
        /// Each time the Runtime (Packages, Apis, Apps, Functions, Workers, etc.) was Started within this process (the first, then any Restarts)
        /// </summary>
        [JsonPropertyName("starts")]
        public List<TrakHoundInstanceRuntimeStart> Starts { get; set; }


        /// <summary>
        /// The time from the process Starting until it was Ready
        /// </summary>
        [JsonIgnore]
        public TimeSpan? BootDuration => Ready != null ? Ready.Value - Started : null;
    }

    /// <summary>
    /// One Start of the Instance Runtime
    /// </summary>
    public class TrakHoundInstanceRuntimeStart
    {
        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        /// <summary>
        /// When the Start finished (Started or failed)
        /// </summary>
        [JsonPropertyName("completed")]
        public DateTime? Completed { get; set; }

        /// <summary>
        /// When the Runtime was Stopped (null if it is still running, or the process ended without Stopping it)
        /// </summary>
        [JsonPropertyName("stopped")]
        public DateTime? Stopped { get; set; }

        /// <summary>
        /// True if the Runtime Started, false if the Start failed
        /// </summary>
        [JsonPropertyName("success")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool Success { get; set; }

        /// <summary>
        /// The Error that stopped the Start (null if it Started)
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }

        /// <summary>
        /// The steps of the Start (ex. Load Packages, Load Workers)
        /// </summary>
        [JsonPropertyName("steps")]
        public List<TrakHoundInstanceBootStep> Steps { get; set; }

        /// <summary>
        /// Warning and Error Log entries during the Start
        /// </summary>
        [JsonPropertyName("log")]
        public TrakHoundInstanceBootLog Log { get; set; }


        [JsonIgnore]
        public TimeSpan? Duration => Completed != null ? Completed.Value - Started : null;
    }

    /// <summary>
    /// A timed step of a Boot or a Runtime Start
    /// </summary>
    public class TrakHoundInstanceBootStep
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        [JsonPropertyName("durationMs")]
        public double DurationMs { get; set; }

        /// <summary>
        /// The Error if the step failed (later steps did not run)
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }
    }

    /// <summary>
    /// The Warning and Error Log entries written during a Boot or a Runtime Start (counts, and the first entries)
    /// </summary>
    public class TrakHoundInstanceBootLog
    {
        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }

        /// <summary>
        /// Error and Critical entries
        /// </summary>
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }

        /// <summary>
        /// The first entries (Errors are kept before Warnings when there are more than fit)
        /// </summary>
        [JsonPropertyName("entries")]
        public List<TrakHoundInstanceBootLogEntry> Entries { get; set; }
    }

    public class TrakHoundInstanceBootLogEntry
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("logger")]
        public string Logger { get; set; }

        /// <summary>
        /// The first line of the Message
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
