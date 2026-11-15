// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// Stream over a WebSocket connection. Messages received from the server are parsed into <typeparamref name="TResult"/>
    /// and raised through the Received event. Push sends a <typeparamref name="TResult"/> to the server
    /// </summary>
    public class TrakHoundWebSocketsStream<TResult> : ITrakHoundStream<TResult>
    {
        private readonly string _streamId;
        private readonly Stream _requestBody;
        private readonly bool _ownsRequestBody;
        private readonly TrakHoundWebSocketsClient _websocketClient;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _disposed;


        /// <summary>
        /// A unique Identifier for the Stream (generated when it is created)
        /// </summary>
        public string Id => _streamId;

        /// <summary>
        /// The CancellationToken that stops the Stream (an internal one is used when none is passed to the constructor)
        /// </summary>
        public CancellationToken CancellationToken => _cancellationToken;

        /// <summary>
        /// The type of the items received (<typeparamref name="TResult"/>)
        /// </summary>
        public Type OutputType => typeof(TResult);

        /// <summary>
        /// Called for each item received
        /// </summary>
        public Action<TResult> OnReceived { get; set; }

        /// <summary>
        /// Called (and awaited) for each item received
        /// </summary>
        public Func<TResult, Task> OnReceivedAsync { get; set; }

        /// <summary>
        /// Parses the bytes of a received message. Takes priority over the default JSON parsing. Ignored when OnInputReceivedAsync is set
        /// </summary>
        public Func<byte[], TResult> OnInputReceived { get; set; }

        /// <summary>
        /// Parses the bytes of a received message. Takes priority over OnInputReceived and the default JSON parsing
        /// </summary>
        public Func<byte[], Task<TResult>> OnInputReceivedAsync { get; set; }

        /// <summary>
        /// Called once when the Stream is disposed
        /// </summary>
        public Action OnDisposed { get; set; }


        /// <summary>
        /// Raised with the Stream Id each time the WebSocket connects (including reconnects)
        /// </summary>
        public event EventHandler<string> Subscribed;

        /// <summary>
        /// Raised for each item received
        /// </summary>
        public event EventHandler<TResult> Received;

        /// <summary>
        /// Raised with the Stream Id when the Stream is disposed
        /// </summary>
        public event EventHandler<string> Disposed;


        /// <summary>
        /// Creates a Stream for the specified Url (relative to the WebSocket Base Url of the Client Configuration)
        /// </summary>
        public TrakHoundWebSocketsStream(TrakHoundHttpClientConfiguration clientConfiguration, string url)
            : this(clientConfiguration, url, null, null, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Url (relative to the WebSocket Base Url of the Client Configuration) that stops when the CancellationToken is cancelled
        /// </summary>
        public TrakHoundWebSocketsStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken)
            : this(clientConfiguration, url, cancellationToken, null, null, false, false) { }

        /// <summary>
        /// Creates a Stream for the specified Url. When sendRequestBody is true, the Request Body is sent after connecting (and disposed with the Stream when ownsRequestBody is true)
        /// </summary>
        protected TrakHoundWebSocketsStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken? cancellationToken, Dictionary<string, string> queryParameters, Stream requestBody, bool ownsRequestBody, bool sendRequestBody)
        {
            _streamId = Guid.NewGuid().ToString();
            _requestBody = requestBody;
            _ownsRequestBody = ownsRequestBody;

            if (cancellationToken.HasValue)
            {
                _cancellationToken = cancellationToken.Value;
            }
            else
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
            }

            var wsUrl = Url.Combine(clientConfiguration?.GetWebSocketBaseUrl(), url);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    wsUrl = Url.AddQueryParameter(wsUrl, queryParameter.Key, queryParameter.Value);
                }
            }

            if (sendRequestBody)
            {
                wsUrl = Url.AddQueryParameter(wsUrl, "requestBody", "true");
                _websocketClient = new TrakHoundWebSocketsClient(wsUrl, _requestBody);
            }
            else
            {
                _websocketClient = new TrakHoundWebSocketsClient(wsUrl);
            }

            _websocketClient.Headers = clientConfiguration?.GetHeaders();
            _websocketClient.ResponseReceived += WebsocketClientResponseReceived;
            _websocketClient.Connected += WebsocketClientConnected;

            // Stop the WebSocket when the caller's token is cancelled
            if (cancellationToken.HasValue) _cancellationRegistration = _cancellationToken.Register(Stop);
        }

        /// <summary>
        /// Stops the WebSocket and releases its resources, then calls OnDisposed and raises Disposed (only the first call has any effect)
        /// </summary>
        /// <summary>
        /// Adds Headers to the WebSocket connection Request (before the Stream connects). They take precedence over the Client Configuration's Headers
        /// </summary>
        internal void AddHeaders(IDictionary<string, string> headers)
        {
            if (headers.IsNullOrEmpty()) return;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_websocketClient.Headers != null)
            {
                foreach (var header in _websocketClient.Headers) merged[header.Key] = header.Value;
            }
            foreach (var header in headers) merged[header.Key] = header.Value;
            _websocketClient.Headers = merged;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _cancellationRegistration.Dispose();

                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }

                _websocketClient.ResponseReceived -= WebsocketClientResponseReceived;
                _websocketClient.Connected -= WebsocketClientConnected;
                _websocketClient.Dispose();

                if (_ownsRequestBody && _requestBody != null) _requestBody.Dispose();

                if (OnDisposed != null) OnDisposed();

                if (Disposed != null) Disposed.Invoke(this, Id);
            }
        }


        /// <summary>
        /// Starts connecting the WebSocket (does nothing if disposed or cancelled)
        /// </summary>
        public void Start()
        {
            if (_disposed == 0 && !_cancellationToken.IsCancellationRequested)
            {
                _websocketClient.Start();
            }
        }

        /// <summary>
        /// Stops the WebSocket connection (does nothing if disposed)
        /// </summary>
        public void Stop()
        {
            if (_disposed == 0)
            {
                _websocketClient.Stop();
            }
        }


        /// <summary>
        /// Default parsing of a received message when neither OnInputReceived nor OnInputReceivedAsync is set
        /// </summary>
        protected virtual TResult Deserialize(byte[] content)
        {
            return JsonSerializer.Deserialize<TResult>(content, Json.SharedOptions);
        }

        /// <summary>
        /// Converts an item to the bytes sent to the server by Push. Return null to skip sending
        /// </summary>
        protected virtual byte[] Serialize(TResult item)
        {
            return Json.Convert(item).ToUtf8Bytes();
        }


        private void WebsocketClientConnected(object sender, EventArgs e)
        {
            try
            {
                if (Subscribed != null) Subscribed.Invoke(this, Id);
            }
            catch { }
        }

        private async void WebsocketClientResponseReceived(object sender, TrakHoundWebSocketResponse response)
        {
            try
            {
                if (response.Content != null)
                {
                    TResult result;
                    if (OnInputReceivedAsync != null) result = await OnInputReceivedAsync(response.Content);
                    else if (OnInputReceived != null) result = OnInputReceived(response.Content);
                    else result = Deserialize(response.Content);

                    // Parsers return default to signal a message that couldn't be parsed
                    if (!IsDefault(result)) await Emit(result);
                }
            }
            catch { }
        }

        private async Task Emit(TResult item)
        {
            if (OnReceived != null) OnReceived(item);
            if (OnReceivedAsync != null) await OnReceivedAsync(item);
            if (Received != null) Received.Invoke(this, item);
        }

        /// <summary>
        /// Send an item to the server. Returns false when it wasn't sent (ex. the WebSocket is not connected yet)
        /// </summary>
        public virtual async Task<bool> Push(TResult item)
        {
            if (!IsDefault(item) && _disposed == 0)
            {
                var bytes = Serialize(item);
                if (bytes != null)
                {
                    return await _websocketClient.Send(bytes);
                }
            }

            return false;
        }

        private static bool IsDefault(TResult item) => EqualityComparer<TResult>.Default.Equals(item, default);
    }
}
