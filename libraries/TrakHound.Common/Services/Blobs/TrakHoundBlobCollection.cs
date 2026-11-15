// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// A Collection of Blobs in an ITrakHoundBlobService
    /// </summary>
    public class TrakHoundBlobCollection
    {
        /// <summary>
        /// True if the Name is set
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Name);

        /// <summary>
        /// The name of the Collection
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The number of Blobs in the Collection
        /// </summary>
        public long Count { get; set; }
    }
}
