// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace TrakHound.Http.Service
{
    public class HttpBlobService : HttpService, ITrakHoundBlobService
    {
        private const string DefaultContentType = "application/octet-stream";


        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.Blobs;


        public HttpBlobService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundBlobCollection>>(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.ListCollections(cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<string>>(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.ListKeys(collection, skip, take, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundBlob>>(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.List(collection, skip, take, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundBlob>(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.Read(collection, key, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<Stream>(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.Download(collection, key, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            // The remote Upload endpoint requires a Content Type. Metadata is not supported by the HTTP API
            if (string.IsNullOrEmpty(contentType)) contentType = DefaultContentType;

            return Forward(await Client.Services.Blobs.Upload(collection, key, content, contentType, overwrite: overwrite, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.Blobs.Delete(collection, key, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
