// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Http;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Streams
{
    /// <summary>
    /// A point-in-time copy of the values charted for the Stream connections
    /// </summary>
    public class StreamSample
    {
        public DateTime Timestamp { get; set; }

        /// <summary>Open connections</summary>
        public double OpenConnections { get; set; }

        /// <summary>Messages sent per second</summary>
        public double MessageSentRate { get; set; }

        /// <summary>Messages received per second</summary>
        public double MessageReceivedRate { get; set; }

        /// <summary>Errors per second</summary>
        public double ErrorRate { get; set; }

        /// <summary>Bytes sent per second</summary>
        public double ByteSentRate { get; set; }

        /// <summary>Bytes received per second</summary>
        public double ByteReceivedRate { get; set; }


        public static StreamSample Create(TrakHoundStreamMetrics metrics, DateTime timestamp)
        {
            var sample = new StreamSample { Timestamp = timestamp };
            if (metrics != null)
            {
                sample.OpenConnections = metrics.OpenConnectionCount;
                sample.MessageSentRate = metrics.MessageSentRate;
                sample.MessageReceivedRate = metrics.MessageReceivedRate;
                sample.ErrorRate = metrics.ErrorRate;
                sample.ByteSentRate = metrics.ByteSentRate;
                sample.ByteReceivedRate = metrics.ByteReceivedRate;
            }
            return sample;
        }

        public double GetValue(string property)
        {
            switch (property)
            {
                case nameof(OpenConnections): return OpenConnections;
                case nameof(MessageSentRate): return MessageSentRate;
                case nameof(MessageReceivedRate): return MessageReceivedRate;
                case nameof(ErrorRate): return ErrorRate;
                case nameof(ByteSentRate): return ByteSentRate;
                case nameof(ByteReceivedRate): return ByteReceivedRate;
                default: return 0;
            }
        }
    }
}
