// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;

namespace TrakHound.Http
{
    /// <summary>
    /// Sends HTTP requests (using <see cref="TrakHoundHttpRequests"/>) with the Authentication headers of a Client Configuration added to each request.
    /// Headers passed to a method take precedence over the Configuration's headers
    /// </summary>
    public class TrakHoundHttpRequestSender
    {
        private readonly TrakHoundHttpClientConfiguration _configuration;


        /// <summary>
        /// Creates a Sender that adds the headers of the Client Configuration (read for each request, so changes take effect immediately)
        /// </summary>
        public TrakHoundHttpRequestSender(TrakHoundHttpClientConfiguration configuration)
        {
            _configuration = configuration;
        }


        /// <summary>
        /// Gets the headers to send: the Configuration's headers, overridden by the headers passed in (null if there are none)
        /// </summary>
        public IDictionary<string, string> GetHeaders(IDictionary<string, string> headers = null)
        {
            var configurationHeaders = _configuration?.GetHeaders();
            if (configurationHeaders.IsNullOrEmpty()) return headers;
            if (headers.IsNullOrEmpty()) return configurationHeaders;

            var merged = new Dictionary<string, string>(configurationHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers) merged[header.Key] = header.Value;
            return merged;
        }


        public Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Send(method, url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Send(method, url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Send(method, url, bytes, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Send(method, url, stream, contentType, GetHeaders(headers), cancellationToken);

        public Task<string> Get(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Get(url, GetHeaders(headers), cancellationToken);

        public Task<T> Get<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Get<T>(url, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> GetResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.GetResponse(url, GetHeaders(headers), cancellationToken);

        public Task<byte[]> GetBytes(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.GetBytes(url, GetHeaders(headers), cancellationToken);

        public Task<Stream> GetStream(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.GetStream(url, GetHeaders(headers), cancellationToken);

        public Task<string> Put(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Put(url, GetHeaders(headers), cancellationToken);

        public Task<T> Put<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Put<T>(url, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PutResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PutResponse(url, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PutResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PutResponse(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<string> Post(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<string> Post(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<bool> Post(string url, IDictionary<string, string> formFields, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post(url, formFields, GetHeaders(headers), cancellationToken);

        public Task<bool> Post(string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post(url, bytes, contentType, GetHeaders(headers), cancellationToken);

        public Task<bool> Post(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post(url, stream, contentType, GetHeaders(headers), cancellationToken);

        public Task<T> Post<T>(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post<T>(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<T> Post<T>(string url, IDictionary<string, string> formFields, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post<T>(url, formFields, GetHeaders(headers), cancellationToken);

        public Task<T> Post<T>(string url, byte[] bytes, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post<T>(url, bytes, contentType, GetHeaders(headers), cancellationToken);

        public Task<T> Post<T>(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post<T>(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<T> Post<T>(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Post<T>(url, stream, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PostResponse(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PostResponse(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PostResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PostResponse(url, body, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PostResponse(string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PostResponse(url, bytes, contentType, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> PostResponse(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.PostResponse(url, stream, contentType, GetHeaders(headers), cancellationToken);

        public Task<bool> Delete(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Delete(url, GetHeaders(headers), cancellationToken);

        public Task<T> Delete<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.Delete<T>(url, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> DeleteResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.DeleteResponse(url, GetHeaders(headers), cancellationToken);

        public Task<TrakHoundHttpResponse> DeleteResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
            => TrakHoundHttpRequests.DeleteResponse(url, body, contentType, GetHeaders(headers), cancellationToken);
    }
}
