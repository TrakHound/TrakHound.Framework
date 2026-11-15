// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Blazor.Services
{
    public class TrakHoundDownloadItem
    {
        public string Key { get; set; }
        public string Filename { get; set; }
        public string ContentType { get; set; }

        /// <summary>
        /// The content (used when FilePath is not set)
        /// </summary>
        public byte[] Content { get; set; }

        /// <summary>
        /// A file to download (streamed from disk, for large content)
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Delete the file at FilePath when the item expires or is removed
        /// </summary>
        public bool DeleteFileWhenRemoved { get; set; }
    }
}
