// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// JSON representation of a TrakHoundBlob
    /// </summary>
    public class TrakHoundBlobJson
    {
        /// <summary>
        /// The name of the Collection that contains the Blob
        /// </summary>
        [JsonPropertyName("collection")]
        public string Collection { get; set; }

        /// <summary>
        /// The Key that identifies the Blob within its Collection
        /// </summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        /// <summary>
        /// The MIME Content Type of the Blob
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// The size of the Blob content (in bytes)
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// When the Blob was created (Unix nanoseconds)
        /// </summary>
        [JsonPropertyName("created")]
        public long Created { get; set; }

        /// <summary>
        /// Additional name/value pairs stored with the Blob
        /// </summary>
        [JsonPropertyName("metadata")]
        public IReadOnlyDictionary<string, string> Metadata { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundBlobJson
        /// </summary>
        public TrakHoundBlobJson() { }

        /// <summary>
        /// Creates a TrakHoundBlobJson from the specified TrakHoundBlob
        /// </summary>
        public TrakHoundBlobJson(TrakHoundBlob blob)
        {
            Collection = blob.Collection;
            Key = blob.Key;
            ContentType = blob.ContentType;
            Size = blob.Size;
            Created = blob.Created;
            Metadata = blob.Metadata;
        }

        /// <summary>
        /// Converts to a TrakHoundBlob
        /// </summary>
        public TrakHoundBlob ToBlob()
        {
            var blob = new TrakHoundBlob();
            blob.Collection = Collection;
            blob.Key = Key;
            blob.ContentType = ContentType;
            blob.Size = Size;
            blob.Created = Created;
            blob.Metadata = Metadata;
            return blob;
        }
    }
}
