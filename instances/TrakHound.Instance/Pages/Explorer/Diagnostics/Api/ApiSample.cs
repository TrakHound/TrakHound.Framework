// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Api
{
    /// <summary>
    /// A point-in-time copy of the values charted for an API
    /// </summary>
    public class ApiSample : IMetricsSample
    {
        public DateTime Timestamp { get; set; }

        /// <summary>Requests per second</summary>
        public double RequestRate { get; set; }

        /// <summary>Errors (Status Code >= 400) per second</summary>
        public double ErrorRate { get; set; }

        /// <summary>Average Response Duration (ms)</summary>
        public double AverageDuration { get; set; }

        /// <summary>Maximum Response Duration (ms)</summary>
        public double MaxDuration { get; set; }

        /// <summary>Request Body bytes per second</summary>
        public double RequestByteRate { get; set; }

        /// <summary>Response Body bytes per second</summary>
        public double ResponseByteRate { get; set; }

        /// <summary>CPU usage (percent of total machine capacity)</summary>
        public double CpuUsage { get; set; }


        public static ApiSample Create(TrakHoundApiMetrics metrics, DateTime timestamp)
        {
            var sample = new ApiSample { Timestamp = timestamp };
            if (metrics != null)
            {
                sample.RequestRate = metrics.RequestRate;
                sample.ErrorRate = metrics.ErrorRate;
                sample.AverageDuration = metrics.AverageDuration;
                sample.MaxDuration = metrics.MaxDuration;
                sample.RequestByteRate = metrics.RequestByteRate;
                sample.ResponseByteRate = metrics.ResponseByteRate;
                sample.CpuUsage = metrics.CpuUsage;
            }
            return sample;
        }

        public double GetValue(string property)
        {
            switch (property)
            {
                case nameof(RequestRate): return RequestRate;
                case nameof(ErrorRate): return ErrorRate;
                case nameof(AverageDuration): return AverageDuration;
                case nameof(MaxDuration): return MaxDuration;
                case nameof(RequestByteRate): return RequestByteRate;
                case nameof(ResponseByteRate): return ResponseByteRate;
                case nameof(CpuUsage): return CpuUsage;
                default: return 0;
            }
        }
    }
}
