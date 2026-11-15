// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundBlobServiceClient that uses the Blob Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpBlobServiceClient : TrakHoundHttpClientBase, ITrakHoundBlobServiceClient
    {
        /// <summary>
        /// Creates a new Blob Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpBlobServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "availability");
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
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "list/collections");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<IEnumerable<TrakHoundBlobCollectionJson>>()?.Select(o => o.ToCollection());
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "list/keys");
            url = Url.AddQueryParameter(url, "collection", collection);
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
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "list");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "skip", skip);
            url = Url.AddQueryParameter(url, "take", take);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<IEnumerable<TrakHoundBlobJson>>()?.Select(o => o.ToBlob());
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix);
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundBlob>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundBlobJson>()?.ToBlob();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "download");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            // Streamed (not buffered) so large Blobs aren't held in memory. The caller disposes the Result, which releases the connection
            var httpResponse = await TrakHoundHttpRequests.GetStreamResponse(url, Requests.GetHeaders(), cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<Stream>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.Content;
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata = null, bool overwrite = true, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix, "upload");
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "contentType", contentType);
            if (!overwrite) url = Url.AddQueryParameter(url, "overwrite", "false");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, content, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Delete(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.BlobServicePrefix);
            url = Url.AddQueryParameter(url, "collection", collection);
            url = Url.AddQueryParameter(url, "key", key);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.DeleteResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }
    }
}
