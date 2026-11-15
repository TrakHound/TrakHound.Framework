// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// Information about a Blob stored by an ITrakHoundBlobService (the content is read separately using Download)
    /// </summary>
    public class TrakHoundBlob
    {
        /// <summary>
        /// True if the Collection, Key and ContentType are set and Created is greater than zero
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Collection) && !string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(ContentType) && Created > 0;

        /// <summary>
        /// The name of the Collection that contains the Blob
        /// </summary>
        public string Collection { get; set; }

        /// <summary>
        /// The Key that identifies the Blob within its Collection
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The MIME Content Type of the Blob (ex. application/json)
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// The size of the Blob content (in bytes)
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// When the Blob was created (Unix nanoseconds)
        /// </summary>
        public long Created { get; set; }

        /// <summary>
        /// Additional name/value pairs stored with the Blob
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; set; }



        /// <summary>
        /// Reads the entire Stream into a byte array (null if the Stream is null or can't be read)
        /// </summary>
        public static byte[] GetContentBytes(Stream contentStream)
        {
            if (contentStream != null)
            {
                try
                {
                    using (var readStream = new MemoryStream())
                    {
                        contentStream.CopyTo(readStream);
                        readStream.Seek(0, SeekOrigin.Begin);
                        return readStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the entire Stream as a UTF-8 string (null if the Stream is null or can't be read)
        /// </summary>
        public static string GetContentUtf8String(Stream contentStream)
        {
            var contentBytes = GetContentBytes(contentStream);
            if (contentBytes != null)
            {
                try
                {
                    return Encoding.UTF8.GetString(contentBytes);
                }
                catch { }
            }

            return null;
        }
    }
}
