// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Metrics for an Operation Buffer's "Store and Forward" File Buffer
    /// </summary>
    public class TrakHoundFileBufferMetrics
    {
        /// <summary>
        /// True if the File Buffer is enabled (set once the Buffer starts)
        /// </summary>
        [JsonPropertyName("isEnabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsEnabled { get; set; }

        /// <summary>
        /// True while the File Buffer has Items waiting to be read (new Items are written to the File Buffer to preserve order)
        /// </summary>
        [JsonPropertyName("isReadActive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsReadActive { get; set; }

        /// <summary>
        /// True if Items were written to the File Buffer during the last Write interval
        /// </summary>
        [JsonPropertyName("isWriteActive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsWriteActive { get; set; }

        /// <summary>
        /// The number of Pages waiting to be read
        /// </summary>
        [JsonPropertyName("pageCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int PageCount { get; set; }

        /// <summary>
        /// The Sequence of the Page most recently read
        /// </summary>
        [JsonPropertyName("readPageSequence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ulong ReadPageSequence { get; set; }

        /// <summary>
        /// The Sequence of the Page most recently written to
        /// </summary>
        [JsonPropertyName("writePageSequence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ulong WritePageSequence { get; set; }

        /// <summary>
        /// The Sequence of the next Page to read. Not currently populated by the Buffer
        /// </summary>
        [JsonPropertyName("nextPageSequence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ulong NextPageSequence { get; set; }

        /// <summary>
        /// The Sequence of the last Page in the File Buffer. Not currently populated by the Buffer
        /// </summary>
        [JsonPropertyName("lastPageSequence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ulong LastPageSequence { get; set; }

        /// <summary>
        /// The maximum size (in bytes) of each Page
        /// </summary>
        [JsonPropertyName("pageSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long PageSize { get; set; }

        /// <summary>
        /// The remaining size (in bytes) of the File Buffer. Not currently populated by the Buffer
        /// </summary>
        [JsonPropertyName("remainingSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long RemainingSize { get; set; }

        /// <summary>
        /// The total number of Items read from the File Buffer (and delivered to the Working Queue)
        /// </summary>
        [JsonPropertyName("totalReadCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalReadCount { get; set; }

        /// <summary>
        /// The total number of Items written to the File Buffer
        /// </summary>
        [JsonPropertyName("totalWriteCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalWriteCount { get; set; }

        /// <summary>
        /// The total number of bytes read from Pages
        /// </summary>
        [JsonPropertyName("totalBytesRead")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesRead { get; set; }

        /// <summary>
        /// The total number of bytes written to Pages
        /// </summary>
        [JsonPropertyName("totalBytesWritten")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalBytesWritten { get; set; }

        /// <summary>
        /// The average number of Items read per second (over the last 10 seconds)
        /// </summary>
        [JsonPropertyName("itemReadRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ItemReadRate { get; set; }

        /// <summary>
        /// The average number of bytes read per second (over the last 10 seconds)
        /// </summary>
        [JsonPropertyName("byteReadRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ByteReadRate { get; set; }

        /// <summary>
        /// The average number of Items written per second (over the last 10 seconds)
        /// </summary>
        [JsonPropertyName("itemWriteRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ItemWriteRate { get; set; }

        /// <summary>
        /// The average number of bytes written per second (over the last 10 seconds)
        /// </summary>
        [JsonPropertyName("byteWriteRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ByteWriteRate { get; set; }

        /// <summary>
        /// The time the File Buffer was last read from or written to (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long LastUpdated { get; set; }
    }
}
