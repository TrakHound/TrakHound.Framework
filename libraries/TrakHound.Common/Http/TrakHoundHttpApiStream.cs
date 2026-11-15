// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using TrakHound.Api;

namespace TrakHound.Clients
{
    /// <summary>
    /// Api Stream over a WebSocket connection that receives TrakHoundApiResponse messages (sent as JSON) from an Api Endpoint
    /// </summary>
    public class TrakHoundHttpApiStream : TrakHoundWebSocketsStream<TrakHoundApiResponse>, ITrakHoundApiStream
    {
        /// <summary>
        /// Creates a Stream for the specified Api Url
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url)
            : base(clientConfiguration, url, null, null, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that stops when the CancellationToken is cancelled
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken)
            : base(clientConfiguration, url, cancellationToken, null, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url with the specified Query Parameters
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, Dictionary<string, string> queryParameters)
            : base(clientConfiguration, url, null, queryParameters, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url with the specified Query Parameters that stops when the CancellationToken is cancelled
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken, Dictionary<string, string> queryParameters)
            : base(clientConfiguration, url, cancellationToken, queryParameters, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the Request Body (serialized as JSON) when it connects
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, null, queryParameters, TrakHoundApiResponse.GetJsonContentStream(requestBody), true, true) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the Request Body (serialized as JSON) when it connects and stops when the CancellationToken is cancelled
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, cancellationToken, queryParameters, TrakHoundApiResponse.GetJsonContentStream(requestBody), true, true) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the Request Body bytes when it connects
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, null, queryParameters, TrakHoundApiResponse.GetContentStream(requestBody), true, true) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the Request Body bytes when it connects and stops when the CancellationToken is cancelled
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, cancellationToken, queryParameters, TrakHoundApiResponse.GetContentStream(requestBody), true, true) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the contents of the Request Body Stream when it connects (the Stream is not disposed by this class)
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, null, queryParameters, requestBody, false, true) { }

        /// <summary>
        /// Creates a Stream for the specified Api Url that sends the contents of the Request Body Stream when it connects and stops when the CancellationToken is cancelled (the Stream is not disposed by this class)
        /// </summary>
        public TrakHoundHttpApiStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null)
            : base(clientConfiguration, url, cancellationToken, queryParameters, requestBody, false, true) { }


        /// <summary>
        /// Parses a received JSON message into a TrakHoundApiResponse (default if the message could not be parsed)
        /// </summary>
        protected override TrakHoundApiResponse Deserialize(byte[] content)
        {
            var json = System.Text.Encoding.UTF8.GetString(content);
            var jsonResponse = Json.Convert<TrakHoundApiJsonResponse>(json);
            return jsonResponse != null ? jsonResponse.ToResponse() : default;
        }

        /// <summary>
        /// Converts a TrakHoundApiResponse to JSON bytes (null if the Response is not valid)
        /// </summary>
        protected override byte[] Serialize(TrakHoundApiResponse response)
        {
            if (response.IsValid())
            {
                var jsonResponse = new TrakHoundApiJsonResponse(response);
                return Json.Convert(jsonResponse).ToUtf8Bytes();
            }

            return null;
        }
    }
}
