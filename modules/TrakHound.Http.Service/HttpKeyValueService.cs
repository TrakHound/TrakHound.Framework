// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Http.Service
{
    public class HttpKeyValueService : HttpService, ITrakHoundKeyValueService
    {
        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.KeyValues;


        public HttpKeyValueService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<string>>(NotConfiguredMessage);

            return Forward(await Client.Services.KeyValues.ListCollections(cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<string>>(NotConfiguredMessage);

            return Forward(await Client.Services.KeyValues.ListKeys(collection, pattern, skip, take, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundKeyValueResponse>(NotConfiguredMessage);

            return Forward(await Client.Services.KeyValues.Read(collection, keys, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<int>(NotConfiguredMessage);

            return Forward(await Client.Services.KeyValues.Count(collection, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            // The TTL is not supported by the HTTP API (the remote Service uses its own default)
            return Forward(await Client.Services.KeyValues.Publish(collection, entries, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.KeyValues.Delete(collection, keys, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
