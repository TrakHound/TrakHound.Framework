// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Http;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Http
{
    /// <summary>
    /// A point-in-time copy of the values charted for the HTTP Requests
    /// </summary>
    public class HttpSample : IMetricsSample
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


        public static HttpSample Create(TrakHoundHttpMetrics metrics, DateTime timestamp)
        {
            var sample = new HttpSample { Timestamp = timestamp };
            if (metrics != null)
            {
                sample.RequestRate = metrics.RequestRate;
                sample.ErrorRate = metrics.ErrorRate;
                sample.AverageDuration = metrics.AverageDuration;
                sample.MaxDuration = metrics.MaxDuration;
                sample.RequestByteRate = metrics.RequestByteRate;
                sample.ResponseByteRate = metrics.ResponseByteRate;
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
                default: return 0;
            }
        }
    }
}
