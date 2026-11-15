// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services.Messages;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// A Publish Stream that sends each Item as a Message frame over a WebSocket to an Instance's Message Service publish stream.
    /// Push returns once the frame is sent; failures reported by the Instance arrive afterwards through PublishFailed
    /// </summary>
    public class TrakHoundHttpMessagePublishStream<T> : TrakHoundMessagePublishStream<T>
    {
        private readonly FrameStream _webSocket;
        private readonly Func<T, byte[]> _createFrame;
        private readonly Func<T, string> _getTopic;
        private TaskCompletionSource _connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;


        /// <summary>
        /// How long Push waits for the WebSocket to connect before the Message fails with 503
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);


        /// <summary>
        /// Creates a Publish Stream for the WebSocket Url (relative to the Client Configuration's WebSocket Base Url). createFrame builds the Message frame for an Item (null if it can't be published)
        /// </summary>
        public TrakHoundHttpMessagePublishStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, Func<T, byte[]> createFrame, Func<T, string> getTopic, CancellationToken cancellationToken = default)
            : base(null, cancellationToken)
        {
            _createFrame = createFrame;
            _getTopic = getTopic;

            _webSocket = new FrameStream(clientConfiguration, url, CancellationToken);
            _webSocket.Subscribed += WebSocketConnected;
            _webSocket.OnReceived = FailureReceived;
            _webSocket.Disposed += WebSocketDisposed;

            Disposed += StreamDisposed;
        }


        /// <summary>
        /// Connects the WebSocket (Push also connects it if the Stream hasn't been started)
        /// </summary>
        protected override void OnStart()
        {
            Interlocked.Exchange(ref _started, 1);
            _webSocket.Start();
        }

        /// <summary>
        /// Closes the WebSocket
        /// </summary>
        protected override void OnStop()
        {
            _webSocket.Stop();
        }

        /// <summary>
        /// Sends the Item's frame, waiting up to ConnectTimeout for the WebSocket to connect
        /// </summary>
        protected override async Task<TrakHoundMessagePublishFailure> Publish(T item, CancellationToken cancellationToken)
        {
            var frame = _createFrame(item);
            if (frame == null) return new TrakHoundMessagePublishFailure(GetTopic(item), 400, "Invalid Message : The Content is null or the Topic contains CR or LF");

            if (Interlocked.Exchange(ref _started, 1) == 0) _webSocket.Start();

            if (await _webSocket.Push(frame)) return null;

            // Not connected yet (or reconnecting). Wait for the next connection, retrying once first in case it connected in between
            var connected = Volatile.Read(ref _connected);
            if (connected.Task.IsCompleted)
            {
                var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                connected = Interlocked.CompareExchange(ref _connected, next, connected) == connected ? next : Volatile.Read(ref _connected);
            }

            if (await _webSocket.Push(frame)) return null;

            try
            {
                await connected.Task.WaitAsync(ConnectTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return new TrakHoundMessagePublishFailure(GetTopic(item), 503, "WebSocket Not Connected");
            }

            if (await _webSocket.Push(frame)) return null;

            return new TrakHoundMessagePublishFailure(GetTopic(item), 503, "WebSocket Not Connected");
        }


        private string GetTopic(T item)
        {
            try { return _getTopic?.Invoke(item); }
            catch { return null; }
        }

        private void WebSocketConnected(object sender, string streamId)
        {
            Volatile.Read(ref _connected).TrySetResult();
        }

        private void FailureReceived(byte[] content)
        {
            TrakHoundMessagePublishFailure failure = null;
            try
            {
                failure = JsonSerializer.Deserialize<TrakHoundMessagePublishFailure>(content, Json.SharedOptions);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }

            if (failure != null) RaisePublishFailed(failure);
        }

        private void WebSocketDisposed(object sender, string streamId)
        {
            Dispose();
        }

        private void StreamDisposed(object sender, string streamId)
        {
            _webSocket.Subscribed -= WebSocketConnected;
            _webSocket.Disposed -= WebSocketDisposed;
            _webSocket.Dispose();
        }


        /// <summary>
        /// Sends and receives raw bytes (the base Stream serializes to JSON)
        /// </summary>
        private sealed class FrameStream : TrakHoundWebSocketsStream<byte[]>
        {
            public FrameStream(TrakHoundHttpClientConfiguration clientConfiguration, string url, CancellationToken cancellationToken)
                : base(clientConfiguration, url, cancellationToken) { }

            protected override byte[] Serialize(byte[] item) => item;

            protected override byte[] Deserialize(byte[] content) => content;
        }
    }
}
