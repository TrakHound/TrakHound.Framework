// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Buffers
{
    /// <summary>
    /// A Configuration used to configure a Buffer
    /// </summary>
    public class TrakHoundBufferConfiguration
    {
        /// <summary>
        /// The interval in milliseconds that asynchronous operations are handled (a negative value uses the default of 5000)
        /// </summary>
        public int Interval { get; set; }

        /// <summary>
        /// The interval in milliseconds to wait before retrying after processing fails (a negative value uses the default of 10000)
        /// </summary>
        public int RetryInterval { get; set; }

        /// <summary>
        /// The maximum number of Items processed per interval (0 uses the default of 5000)
        /// </summary>
        public int MaxItemsPerInterval { get; set; }

        /// <summary>
        /// The maximum number of Items held in the in-memory Working Queue (0 uses the default of 25000)
        /// </summary>
        public int QueuedItemLimit { get; set; }

        /// <summary>
        /// Determines whether the "Store and Forward" File Buffer is enabled
        /// </summary>
        public bool FileBufferEnabled { get; set; }

        /// <summary>
        /// Determines whether new Items are forced to the File Buffer instead of the Working Queue
        /// </summary>
        public bool FileBufferForceEnabled { get; set; }

        /// <summary>
        /// The size (in bytes) of the Maximum Page Size to use for the File Buffer
        /// </summary>
        public int FileBufferPageSize { get; set; }

        /// <summary>
        /// The interval in milliseconds at which the File Buffer is read
        /// </summary>
        public int FileBufferReadInterval { get; set; }

        /// <summary>
        /// The interval in milliseconds at which the File Buffer is written
        /// </summary>
        public int FileBufferWriteInterval { get; set; }

        /// <summary>
        /// Determines whether messages are verified after successfully sending
        /// </summary>
        public bool AcknowledgeSent { get; set; }


        /// <summary>
        /// Creates a new Buffer Configuration with every value unset (so the Buffer's defaults are used) and the File Buffer disabled
        /// </summary>
        public TrakHoundBufferConfiguration()
        {
            Interval = -1;
            RetryInterval = -1;
            MaxItemsPerInterval = 0;
            QueuedItemLimit = 0;
            FileBufferEnabled = false;
            FileBufferForceEnabled = false;
            FileBufferPageSize = 0;
            FileBufferReadInterval = -1;
            FileBufferWriteInterval = -1;
            AcknowledgeSent = false;
        }
    }
}
