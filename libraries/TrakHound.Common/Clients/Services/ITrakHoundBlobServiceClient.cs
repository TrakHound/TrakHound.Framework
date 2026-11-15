// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to list, read, upload, and delete Blobs in the Blob Service
    /// </summary>
    public interface ITrakHoundBlobServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Lists the Blob Collections available through the Router
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Blob Keys in the specified Collection (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Blobs (Key, Content Type, Size, Created) in the specified Collection (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Blob information (Key, Content Type, Size, Created) for the specified Key without its content
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads the content of the Blob with the specified Key as a Stream
        /// </summary>
        Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads the content as a Blob with the specified Key, Content Type, and metadata. If overwrite is false, an existing Blob is not replaced
        /// </summary>
        Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType = "application/octet-stream", IReadOnlyDictionary<string, string> metadata = null, bool overwrite = true, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the Blob with the specified Key
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string collection, string key, string routerId = null, CancellationToken cancellationToken = default);
    }
}
