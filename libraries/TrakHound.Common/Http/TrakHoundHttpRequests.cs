// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Http
{
    /// <summary>
    /// Static helpers for sending HTTP requests using a shared HttpClient. Methods do not throw; failures are indicated by the return value
    /// </summary>
    public static class TrakHoundHttpRequests
    {
        private const int DefaultTimeout = 300000;

        private static readonly HttpClient httpClient = new HttpClient(CreateHandler())
        {
            // Timeout is enforced per request (see SendCore) so that changes to the Timeout property take effect
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        /// <summary>
        /// Request Timeout in Milliseconds
        /// </summary>
        public static int Timeout { get; set; } = DefaultTimeout;

        /// <summary>
        /// Allow connections to servers with untrusted (ex. self-signed) certificates
        /// </summary>
        public static bool AllowUntrustedCertificates { get; set; } = true;


        // TrakHound.Client copies this file and also builds it for .NET Standard 2.0 (ex. .NET Framework), which has no SocketsHttpHandler or Brotli
        private static HttpMessageHandler CreateHandler()
        {
#if NET
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, certChain, policyErrors) =>
                    {
                        return AllowUntrustedCertificates || policyErrors == SslPolicyErrors.None;
                    }
                }
            };
#else
            return new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (request, cert, certChain, policyErrors) =>
                {
                    return AllowUntrustedCertificates || policyErrors == SslPolicyErrors.None;
                }
            };
#endif
        }


        #region "Send"

        /// <summary>
        /// Send a request with a UTF-8 string body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="method">HTTP method of the request</param>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">String to send as the UTF-8 encoded request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(method, url, CreateContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a request with an object serialized as a JSON body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="method">HTTP method of the request</param>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(method, url, CreateJsonContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a request with a binary body (omitted if empty) and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="method">HTTP method of the request</param>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="bytes">Bytes to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(method, url, !bytes.IsNullOrEmpty() ? CreateContent(bytes, contentType) : null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a request with a Stream body and return the full response. The Stream is disposed after the request is sent. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="method">HTTP method of the request</param>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="stream">Stream to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> Send(HttpMethod method, string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(method, url, CreateContent(stream, contentType), headers, cancellationToken);
        }

        #endregion

        #region "Get"

        /// <summary>
        /// Send a GET request and return the response body as a string (regardless of status code), or null if the request fails
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<string> Get(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendString(HttpMethod.Get, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a GET request and deserialize the JSON response body, or return default if the request fails or returns a non-success status code
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Get<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendJson<T>(HttpMethod.Get, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a GET request and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> GetResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Get, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a GET request and return the response body as bytes (regardless of status code), or null if the body is empty or the request fails
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static async Task<byte[]> GetBytes(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await SendCore(HttpMethod.Get, url, null, headers, cancellationToken).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return bytes.Length > 0 ? bytes : null;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Send a GET request and return the response body as a Stream, or null if the request fails or returns a non-success status code. The caller is responsible for disposing the Stream
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static async Task<Stream> GetStream(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response = null;

            try
            {
                // The body is streamed from the connection, so the returned Stream owns (and disposes) the response
                response = await SendCore(HttpMethod.Get, url, null, headers, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return new HttpResponseStream(stream, response);
            }
            catch
            {
                response?.Dispose();
            }

            return null;
        }

        /// <summary>
        /// Send a GET request and return the response without buffering a success body: <see cref="TrakHoundHttpResponse.Content"/> is read from the connection and must be disposed by the caller
        /// (it is not a <see cref="MemoryStream"/>, so it can only be read once). Non-success bodies are buffered. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. Getting the response headers is also canceled after <see cref="Timeout"/> milliseconds (reading the body is not)</param>
        public static async Task<TrakHoundHttpResponse> GetStreamResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response = null;

            try
            {
                response = await SendCore(HttpMethod.Get, url, null, headers, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                // Error bodies are small: buffer them so the connection is released even if the caller ignores the Content
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
                {
                    using var bufferedResponse = response;
                    response = null;

                    using var timeoutCts = CreateTimeoutSource(cancellationToken);
                    var bytes = await bufferedResponse.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
                    return new TrakHoundHttpResponse(bufferedResponse, CreateBufferedContent(bytes));
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var streamResponse = new TrakHoundHttpResponse(response, new HttpResponseStream(stream, response));
                response = null; // Now owned by the Stream
                return streamResponse;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                response?.Dispose();
                return new TrakHoundHttpResponse { Message = ex.Message };
            }
        }

        #endregion

        #region "Put"

        /// <summary>
        /// Send a PUT request (no body) and return the response body as a string (regardless of status code), or null if the request fails
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<string> Put(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendString(HttpMethod.Put, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a PUT request (no body) and deserialize the JSON response body, or return default if the request fails or returns a non-success status code
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Put<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendJson<T>(HttpMethod.Put, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a PUT request (no body) and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PutResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Put, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a PUT request with an object serialized as a JSON body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PutResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Put, url, CreateJsonContent(body, contentType), headers, cancellationToken);
        }

        #endregion

        #region "Post"

        /// <summary>
        /// Send a POST request with a UTF-8 string body (omitted if empty) and return the response body as a string (regardless of status code), or null if the request fails
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">String to send as the UTF-8 encoded request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<string> Post(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendString(HttpMethod.Post, url, !string.IsNullOrEmpty(body) ? CreateContent(body, contentType) : null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with an object serialized as a JSON body and return the response body as a string (regardless of status code), or null if the request fails
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<string> Post(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendString(HttpMethod.Post, url, CreateJsonContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a URL-encoded form body and return true if a success status code is returned
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="formFields">Fields to send as a URL-encoded form (application/x-www-form-urlencoded) request body</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<bool> Post(string url, IDictionary<string, string> formFields, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendSuccess(HttpMethod.Post, url, CreateContent(formFields), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a binary body and return true if a success status code is returned
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="bytes">Bytes to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<bool> Post(string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendSuccess(HttpMethod.Post, url, CreateContent(bytes, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a Stream body and return true if a success status code is returned. Returns false without sending if the Stream is null. The Stream is disposed after the request is sent
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="stream">Stream to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<bool> Post(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            if (stream == null) return Task.FromResult(false);

            return SendSuccess(HttpMethod.Post, url, CreateContent(stream, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a UTF-8 string body and deserialize the JSON response body, or return default if the request fails or returns a non-success status code. Returns default without sending if the body is null
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">String to send as the UTF-8 encoded request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Post<T>(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            if (body == null) return Task.FromResult<T>(default);

            return SendJson<T>(HttpMethod.Post, url, CreateContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a URL-encoded form body and deserialize the JSON response body, or return default if the request fails or returns a non-success status code
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="formFields">Fields to send as a URL-encoded form (application/x-www-form-urlencoded) request body</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Post<T>(string url, IDictionary<string, string> formFields, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendJson<T>(HttpMethod.Post, url, CreateContent(formFields), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a binary body and deserialize the JSON response body, or return default if the request fails or returns a non-success status code
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="bytes">Bytes to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Post<T>(string url, byte[] bytes, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendJson<T>(HttpMethod.Post, url, CreateContent(bytes, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with an object serialized as a JSON body and deserialize the JSON response body, or return default if the request fails or returns a non-success status code. Returns default without sending if the body is null or serializes to empty
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Post<T>(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            var content = CreateJsonContent(body, contentType);
            if (content == null) return Task.FromResult<T>(default);

            return SendJson<T>(HttpMethod.Post, url, content, headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a Stream body and deserialize the JSON response body, or return default if the request fails or returns a non-success status code. Returns default without sending if the Stream is null. The Stream is disposed after the request is sent
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="stream">Stream to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Post<T>(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            if (stream == null) return Task.FromResult<T>(default);

            return SendJson<T>(HttpMethod.Post, url, CreateContent(stream, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a UTF-8 string body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">String to send as the UTF-8 encoded request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PostResponse(string url, string body, string contentType = "text/plain", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Post, url, CreateContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with an object serialized as a JSON body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PostResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Post, url, CreateJsonContent(body, contentType), headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a binary body (omitted if empty) and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="bytes">Bytes to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PostResponse(string url, byte[] bytes, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Post, url, !bytes.IsNullOrEmpty() ? CreateContent(bytes, contentType) : null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a POST request with a Stream body and return the full response. The Stream is disposed after the request is sent. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="stream">Stream to send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> PostResponse(string url, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Post, url, CreateContent(stream, contentType), headers, cancellationToken);
        }

        #endregion

        #region "Delete"

        /// <summary>
        /// Send a DELETE request and return true if a success status code is returned
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<bool> Delete(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendSuccess(HttpMethod.Delete, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a DELETE request and deserialize the JSON response body, or return default if the request fails or returns a non-success status code
        /// </summary>
        /// <typeparam name="T">Type to deserialize the JSON response body to</typeparam>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<T> Delete<T>(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendJson<T>(HttpMethod.Delete, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a DELETE request and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> DeleteResponse(string url, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Delete, url, null, headers, cancellationToken);
        }

        /// <summary>
        /// Send a DELETE request with an object serialized as a JSON body and return the full response. Errors are returned in <see cref="TrakHoundHttpResponse.Message"/>
        /// </summary>
        /// <param name="url">Absolute URL to send the request to</param>
        /// <param name="body">Object to serialize to JSON and send as the request body</param>
        /// <param name="contentType">Media type of the request body (Content-Type header)</param>
        /// <param name="headers">Additional headers to add to the request. Content headers (ex. Content-Type) are applied to the request body</param>
        /// <param name="cancellationToken">Token to cancel the request. The request is also canceled after <see cref="Timeout"/> milliseconds</param>
        public static Task<TrakHoundHttpResponse> DeleteResponse(string url, object body, string contentType = "application/json", IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return SendResponse(HttpMethod.Delete, url, CreateJsonContent(body, contentType), headers, cancellationToken);
        }

        #endregion


        #region "Core"

        private static Task<HttpResponseMessage> SendCore(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return SendCore(method, url, content, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendCore(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Content = content;

            if (!headers.IsNullOrEmpty())
            {
                foreach (var header in headers)
                {
                    // Content headers (ex. Content-Type) are not valid on the request itself
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            using var timeoutCts = CreateTimeoutSource(cancellationToken);

            // ResponseContentRead buffers the full response, so reading it after the timeout source is disposed is safe.
            // With ResponseHeadersRead the timeout only covers getting the headers (the body is read later without it)
            return await httpClient.SendAsync(request, completionOption, timeoutCts.Token).ConfigureAwait(false);
        }

        private static CancellationTokenSource CreateTimeoutSource(CancellationToken cancellationToken)
        {
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (Timeout > 0) timeoutCts.CancelAfter(Timeout);
            return timeoutCts;
        }

        // Publicly visible so the bytes can be read with TryGetBuffer (ex. to deserialize JSON without a copy)
        private static Stream CreateBufferedContent(byte[] bytes)
        {
            return bytes.Length > 0 ? new MemoryStream(bytes, 0, bytes.Length, true, true) : null;
        }

        private static async Task<TrakHoundHttpResponse> SendResponse(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await SendCore(method, url, content, headers, cancellationToken).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                return new TrakHoundHttpResponse(response, CreateBufferedContent(bytes));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new TrakHoundHttpResponse { Message = ex.Message };
            }
        }

        private static async Task<string> SendString(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await SendCore(method, url, content, headers, cancellationToken).ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return null;
        }

        private static async Task<T> SendJson<T>(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await SendCore(method, url, content, headers, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                // Deserialize straight from the bytes (no intermediate string). Invalid JSON is caught below and returns default
                var json = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (json.Length > 0)
                {
                    return JsonSerializer.Deserialize<T>(json, Json.SharedOptions);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return default;
        }

        private static async Task<bool> SendSuccess(HttpMethod method, string url, HttpContent content, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await SendCore(method, url, content, headers, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return false;
        }

        #endregion

        #region "Content"

        private static HttpContent CreateContent(string body, string contentType)
        {
            if (body == null) return null;

            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return content;
        }

        private static HttpContent CreateJsonContent(object body, string contentType)
        {
            if (body == null) return null;

            var json = Json.Convert(body);
            return !string.IsNullOrEmpty(json) ? CreateContent(json, contentType) : null;
        }

        private static HttpContent CreateContent(byte[] bytes, string contentType)
        {
            if (bytes == null) return null;

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return content;
        }

        private static HttpContent CreateContent(Stream stream, string contentType)
        {
            if (stream == null) return null;

            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return content;
        }

        private static HttpContent CreateContent(IDictionary<string, string> formFields)
        {
            return formFields != null ? new FormUrlEncodedContent(formFields) : null;
        }

        #endregion

        #region "Response Stream"

        // A response body read from the connection (ResponseHeadersRead). Disposing it disposes the response, which releases the connection
        private sealed class HttpResponseStream : Stream
        {
            private readonly Stream _inner;
            private readonly HttpResponseMessage _response;


            public HttpResponseStream(Stream inner, HttpResponseMessage response)
            {
                _inner = inner;
                _response = response;
            }


            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            // The Content-Length when the server sent one (it is removed when the body is decompressed), so callers that read Length still work
            public override long Length => _response.Content.Headers.ContentLength ?? _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
#if NET
            public override int Read(Span<byte> buffer) => _inner.Read(buffer);
#endif
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
#if NET
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
#endif
            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => _inner.CopyToAsync(destination, bufferSize, cancellationToken);

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _response.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        #endregion
    }
}
