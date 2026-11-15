// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundKeyValueServiceClient that uses the KeyValue Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpKeyValueServiceClient : TrakHoundHttpClientBase, ITrakHoundKeyValueServiceClient
    {
        /// <summary>
        /// Creates a new KeyValue Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpKeyValueServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "availability");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundServiceAvailabilityResponse>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "list/collections");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<IEnumerable<string>>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<IEnumerable<string>>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "list/keys");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "pattern", pattern);
            url = Url.AddQueryParameter(url, "skip", skip);
            url = Url.AddQueryParameter(url, "take", take);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<IEnumerable<string>>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<IEnumerable<string>>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<string>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix);
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<string>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetContentUtf8String();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix);
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, keys, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundKeyValueResponse>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;

            // Endpoint returns only the Values (Key => Value)
            var values = httpResponse.GetJsonContentObject<Dictionary<string, string>>();
            if (values != null)
            {
                var keyValueResponse = new TrakHoundKeyValueResponse();
                keyValueResponse.Collection = collection;
                keyValueResponse.Values = values;
                serviceResponse.Result = keyValueResponse;
            }

            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<int>> Count(string collection, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "count");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<int>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetContentUtf8String().ToInt();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Publish(string collection, string key, string value, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "publish");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "value", value);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PutResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix, "publish");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, entries, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.KeyValueServicePrefix);
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.DeleteResponse(url, (object)keys, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }
    }
}
