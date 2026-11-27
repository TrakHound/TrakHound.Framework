// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Mcp;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Mcp
{
    /// <summary>
    /// A point-in-time copy of the values charted for the MCP Server
    /// </summary>
    public class McpSample : IMetricsSample
    {
        public DateTime Timestamp { get; set; }

        /// <summary>Tool calls per second</summary>
        public double CallRate { get; set; }

        /// <summary>Tool calls that returned an error, per second</summary>
        public double ErrorRate { get; set; }

        /// <summary>Average Tool call duration (milliseconds)</summary>
        public double AverageDuration { get; set; }

        /// <summary>Longest Tool call duration (milliseconds)</summary>
        public double MaxDuration { get; set; }


        public static McpSample Create(TrakHoundMcpMetrics metrics, DateTime timestamp)
        {
            var sample = new McpSample { Timestamp = timestamp };
            if (metrics != null)
            {
                sample.CallRate = metrics.CallRate;
                sample.ErrorRate = metrics.ErrorRate;
                sample.AverageDuration = metrics.AverageDuration;
                sample.MaxDuration = metrics.MaxDuration;
            }
            return sample;
        }

        public double GetValue(string property)
        {
            switch (property)
            {
                case nameof(CallRate): return CallRate;
                case nameof(ErrorRate): return ErrorRate;
                case nameof(AverageDuration): return AverageDuration;
                case nameof(MaxDuration): return MaxDuration;
                default: return 0;
            }
        }
    }
}
