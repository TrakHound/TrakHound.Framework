// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Logging;

namespace TrakHound.Instance.Components
{
    /// <summary>
    /// A line shown by LogRows. Create it once per received Log Item (its Key is the row's render key)
    /// </summary>
    public class LogRow
    {
        /// <summary>
        /// Unique within a log (the same Log Item can be received more than once, and duplicate render keys break rendering)
        /// </summary>
        public long Key { get; set; }

        public TrakHoundLogLevel Level { get; set; }

        /// <summary>
        /// Local time
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Where the line came from (shown when LogRows.ShowSource is set)
        /// </summary>
        public string Source { get; set; }

        public string Message { get; set; }


        public static LogRow Create(TrakHoundLogItem item, long key)
        {
            var row = new LogRow();
            row.Key = key;
            row.Level = item.LogLevel;
            row.Timestamp = item.Timestamp.ToLocalDateTime();
            row.Source = item.Sender;
            row.Message = item.Message;
            return row;
        }
    }
}
