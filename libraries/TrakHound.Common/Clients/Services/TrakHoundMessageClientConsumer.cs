// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services.Messages;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Stream that receives Message Service Messages from a WebSocket connection (obsolete)
    /// </summary>
    [Obsolete("Use ITrakHoundMessageServiceClient.Subscribe() which returns an ITrakHoundStream<TrakHoundMessageResponse>")]
    public class TrakHoundMessageClientConsumer : ITrakHoundStream<TrakHoundMessageResponse>
    {
        private readonly TrakHoundWebSocketsClient _websocketClient;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly string _consumerId;
        private bool _disposed;


        /// <summary>
        /// The unique ID of the Consumer (a new Guid)
        /// </summary>
        public string Id => _consumerId;

        /// <summary>
        /// Token that is cancelled when the Consumer is disposed (or the Token passed to the constructor)
        /// </summary>
        public CancellationToken CancellationToken => _cancellationToken;

        /// <summary>
        /// The type of the items received (TrakHoundMessageResponse)
        /// </summary>
        public Type OutputType => typeof(TrakHoundMessageResponse);

        /// <summary>
        /// Called when a Message is received
        /// </summary>
        public Action<TrakHoundMessageResponse> OnReceived { get; set; }

        /// <summary>
        /// Called (and awaited) when a Message is received
        /// </summary>
        public Func<TrakHoundMessageResponse, Task> OnReceivedAsync { get; set; }

        /// <summary>
        /// Called when the Consumer is disposed
        /// </summary>
        public Action OnDisposed { get; set; }


        /// <summary>
        /// Raised when a Message is received
        /// </summary>
        public event EventHandler<TrakHoundMessageResponse> Received;

        /// <summary>
        /// Raised when the Consumer is disposed, with the Consumer ID
        /// </summary>
        public event EventHandler<string> Disposed;


        /// <summary>
        /// Creates a new Consumer for the WebSocket route (relative to the Client Configuration's WebSocket base URL)
        /// </summary>
        public TrakHoundMessageClientConsumer(TrakHoundHttpClientConfiguration clientConfiguration, string url)
            : this(clientConfiguration, url, null, null) { }

        /// <summary>
        /// Creates a new Consumer for the WebSocket route that uses the specified CancellationToken
        /// </summary>
        public TrakHoundMessageClientConsumer(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken)
            : this(clientConfiguration, url, cancellationToken, null) { }

        /// <summary>
        /// Creates a new Consumer for the WebSocket route that sends the request body when connected
        /// </summary>
        public TrakHoundMessageClientConsumer(TrakHoundHttpClientConfiguration clientConfiguration, string url, object requestBody)
            : this(clientConfiguration, url, null, requestBody) { }

        /// <summary>
        /// Creates a new Consumer for the WebSocket route that sends the request body and uses the specified CancellationToken
        /// </summary>
        public TrakHoundMessageClientConsumer(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken, object requestBody)
            : this(clientConfiguration, url, (CancellationToken?)cancellationToken, requestBody) { }

        private TrakHoundMessageClientConsumer(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken? cancellationToken, object requestBody)
        {
            _consumerId = Guid.NewGuid().ToString();

            if (cancellationToken.HasValue)
            {
                _cancellationToken = cancellationToken.Value;
            }
            else
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
            }

            var websocketUrl = Url.Combine(clientConfiguration?.GetWebSocketBaseUrl(), url);
            _websocketClient = requestBody != null ? new TrakHoundWebSocketsClient(websocketUrl, requestBody) : new TrakHoundWebSocketsClient(websocketUrl);
            _websocketClient.Headers = clientConfiguration?.GetHeaders();
            _websocketClient.ResponseReceived += WebsocketClientResponseReceived;
        }

        /// <summary>
        /// Cancels the Consumer and closes the WebSocket connection
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                _cancellationTokenSource?.Cancel();

                if (_websocketClient != null)
                {
                    _websocketClient.ResponseReceived -= WebsocketClientResponseReceived;
                    _websocketClient.Dispose();
                }

                OnDisposed?.Invoke();
                Disposed?.Invoke(this, Id);
            }
        }


        /// <summary>
        /// Starts the WebSocket connection
        /// </summary>
        public void Start()
        {
            _websocketClient.Start();
        }

        /// <summary>
        /// Stops the WebSocket connection
        /// </summary>
        public void Stop()
        {
            _websocketClient.Stop();
        }

        private async void WebsocketClientResponseReceived(object sender, TrakHoundWebSocketResponse response)
        {
            // async void: any exception here would crash the process, so nothing may escape
            try
            {
                if (response.Content != null)
                {
                    var messageResponse = TrakHoundHttpMessageResponse.Parse(response.Content);
                    if (messageResponse.IsValid)
                    {
                        await Push(messageResponse);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Raises the received callbacks and event for the Message. Always returns true
        /// </summary>
        public virtual async Task<bool> Push(TrakHoundMessageResponse item)
        {
            OnReceived?.Invoke(item);
            if (OnReceivedAsync != null) await OnReceivedAsync(item);
            Received?.Invoke(this, item);

            return true;
        }
    }
}
