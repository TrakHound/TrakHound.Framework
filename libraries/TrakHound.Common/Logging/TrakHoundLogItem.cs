// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Logging
{
    /// <summary>
    /// A Log Entry
    /// </summary>
    public struct TrakHoundLogItem
    {
        /// <summary>
        /// The Logger Name (plus an optional sub-name) that wrote the Entry
        /// </summary>
        public string Sender { get; set; }

        /// <summary>
        /// The severity of the Entry
        /// </summary>
        public TrakHoundLogLevel LogLevel { get; set; }

        /// <summary>
        /// The Message of the Entry
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// An optional Code identifying the type of Entry
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// When the Entry was written (Unix Timestamp in nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }


        /// <summary>
        /// Creates a new Log Entry (the Timestamp defaults to now if 0)
        /// </summary>
        public TrakHoundLogItem(string sender, TrakHoundLogLevel logLevel, string message, string code = null, long timestamp = 0)
        {
            Sender = sender;
            LogLevel = logLevel;
            Message = message;
            Code = code;
            Timestamp = timestamp > 0 ? timestamp : UnixDateTime.Now;
        }
    }
}
