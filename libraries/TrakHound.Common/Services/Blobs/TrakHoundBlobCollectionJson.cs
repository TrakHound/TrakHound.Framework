// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// JSON representation of a TrakHoundBlobCollection
    /// </summary>
    public class TrakHoundBlobCollectionJson
    {
        /// <summary>
        /// The name of the Collection
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The number of Blobs in the Collection
        /// </summary>
        [JsonPropertyName("count")]
        public long Count { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundBlobCollectionJson
        /// </summary>
        public TrakHoundBlobCollectionJson() { }

        /// <summary>
        /// Creates a TrakHoundBlobCollectionJson from the specified TrakHoundBlobCollection
        /// </summary>
        public TrakHoundBlobCollectionJson(TrakHoundBlobCollection collection)
        {
            Name = collection.Name;
            Count = collection.Count;
        }

        /// <summary>
        /// Converts to a TrakHoundBlobCollection
        /// </summary>
        public TrakHoundBlobCollection ToCollection()
        {
            var collection = new TrakHoundBlobCollection();
            collection.Name = Name;
            collection.Count = Count;
            return collection;
        }
    }
}
