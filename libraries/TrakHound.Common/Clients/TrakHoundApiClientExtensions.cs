// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Api;

namespace TrakHound.Clients
{
    /// <summary>
    /// Overloads of ITrakHoundApiClient.Send that pass Headers (read by the Endpoint's [FromHeader] Parameters). Headers that carry credentials
    /// for the Instance are never passed to an Api (see TrakHoundApiHeaders.Withheld)
    /// </summary>
    public static class TrakHoundApiClientExtensions
    {
        /// <summary>
        /// Sends a GET Request with query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Get(this ITrakHoundApiClient client, string route, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.GET, route, null, null, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a POST Request with a body serialized as JSON (by default), query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Post(this ITrakHoundApiClient client, string route, object requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.POST, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a POST Request with a byte array body, query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Post(this ITrakHoundApiClient client, string route, byte[] requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.POST, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a POST Request with a Stream body, query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Post(this ITrakHoundApiClient client, string route, Stream requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.POST, route, requestBody, contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a PUT Request with a body serialized as JSON (by default), query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Put(this ITrakHoundApiClient client, string route, object requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.PUT, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a PUT Request with a byte array body, query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Put(this ITrakHoundApiClient client, string route, byte[] requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.PUT, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a PUT Request with a Stream body, query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Put(this ITrakHoundApiClient client, string route, Stream requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.PUT, route, requestBody, contentType, queryParameters, headers, cancellationToken);

        /// <summary>
        /// Sends a DELETE Request with query parameters and Headers to the specified Api route and returns the Api Response
        /// </summary>
        public static Task<TrakHoundApiResponse> Delete(this ITrakHoundApiClient client, string route, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
            => client.Send(TrakHoundApiMethod.DELETE, route, null, null, queryParameters, headers, cancellationToken);
    }
}
