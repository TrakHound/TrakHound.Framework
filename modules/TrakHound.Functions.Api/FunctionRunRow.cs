// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Functions
{
    /// <summary>
    /// A row of the Run query (see Controller.GetFunctionRun) : a Run with its Trigger and Detail (null when they weren't recorded)
    /// </summary>
    internal class FunctionRunRow
    {
        public string Id { get; set; }

        public string FunctionId { get; set; }

        public int StatusCode { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public TimeSpan Duration { get; set; }

        public string Trigger { get; set; }

        public string TaskId { get; set; }

        public string PackageVersion { get; set; }

        public string JobId { get; set; }

        public double? CpuTime { get; set; }

        public double? CpuUsage { get; set; }
    }
}
