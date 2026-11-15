// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Buffers;

namespace TrakHound.Instance.Pages.Explorer.Diagnostics.Buffers
{
    /// <summary>
    /// A point-in-time copy of the values charted for a Buffer
    /// </summary>
    public class BufferSample
    {
        public DateTime Timestamp { get; set; }

        public double QueueItems { get; set; }

        /// <summary>Items entering the Working Queue (items/s)</summary>
        public double QueueRate { get; set; }

        /// <summary>Items written to the File Buffer (items/s)</summary>
        public double FileWriteRate { get; set; }

        /// <summary>Items read from the File Buffer (items/s)</summary>
        public double FileReadRate { get; set; }

        /// <summary>Bytes written to the File Buffer (bytes/s)</summary>
        public double FileByteWriteRate { get; set; }

        /// <summary>Bytes read from the File Buffer (bytes/s)</summary>
        public double FileByteReadRate { get; set; }


        public static BufferSample Create(TrakHoundBufferMetrics metrics, DateTime timestamp)
        {
            return new BufferSample
            {
                Timestamp = timestamp,
                QueueItems = metrics.Queue?.ItemCount ?? 0,
                QueueRate = metrics.Queue?.ItemRate ?? 0,
                FileWriteRate = metrics.FileBuffer?.ItemWriteRate ?? 0,
                FileReadRate = metrics.FileBuffer?.ItemReadRate ?? 0,
                FileByteWriteRate = metrics.FileBuffer?.ByteWriteRate ?? 0,
                FileByteReadRate = metrics.FileBuffer?.ByteReadRate ?? 0
            };
        }

        public double GetValue(string property)
        {
            switch (property)
            {
                case nameof(QueueItems): return QueueItems;
                case nameof(QueueRate): return QueueRate;
                case nameof(FileWriteRate): return FileWriteRate;
                case nameof(FileReadRate): return FileReadRate;
                case nameof(FileByteWriteRate): return FileByteWriteRate;
                case nameof(FileByteReadRate): return FileByteReadRate;
                default: return 0;
            }
        }
    }
}
