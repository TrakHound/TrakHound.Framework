// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// A Service that stores Blobs (binary content with a Content Type and Metadata) organized by Collection and Key
    /// </summary>
    public interface ITrakHoundBlobService : ITrakHoundService
    {
        /// <summary>
        /// Lists the Blob Collections and the number of Blobs in each
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Keys of the Blobs in the specified Collection (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Blobs (without content) in the specified Collection (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the information (without content) of the Blob with the specified Collection and Key
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a Stream to read the content of the Blob with the specified Collection and Key
        /// </summary>
        Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads content to the Blob with the specified Collection and Key
        /// (when overwrite is false, an existing Blob is not replaced)
        /// </summary>
        Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, bool overwrite = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the Blob with the specified Collection and Key
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string collection, string key, CancellationToken cancellationToken = default);
    }
}
