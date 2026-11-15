// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Services
{
    /// <summary>
    /// A point-in-time copy of the values charted for a Service
    /// </summary>
    public class ServiceSample
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


        public static ServiceSample Create(TrakHoundServiceMetrics metrics, DateTime timestamp)
        {
            var sample = new ServiceSample { Timestamp = timestamp };
            if (metrics != null)
            {
                sample.RequestRate = metrics.RequestRate;
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
                case nameof(RequestRate): return RequestRate;
                case nameof(ErrorRate): return ErrorRate;
                case nameof(AverageDuration): return AverageDuration;
                case nameof(MaxDuration): return MaxDuration;
                default: return 0;
            }
        }
    }
}
