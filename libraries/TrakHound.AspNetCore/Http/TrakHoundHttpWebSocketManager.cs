// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net.WebSockets;
using TrakHound.Logging;

namespace TrakHound.Http
{
    /// <summary>
    /// Connects HTTP Stream Requests to TrakHound Streams. WebSocket Requests are handled here,
    /// and Server-Sent Events Requests (Accept: text/event-stream) by the TrakHoundHttpEventStreamManager
    /// </summary>
    public class TrakHoundHttpWebSocketManager : IAsyncDisposable
    {
        /// <summary>
        /// Largest message accepted from a client. Larger messages close the WebSocket with MessageTooBig
        /// </summary>
        private const int MaxInputMessageSize = 16 * 1024 * 1024;

        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundHttpWebSocketManager>();
        private readonly Dictionary<string, WebSocket> _webSockets = new Dictionary<string, WebSocket>(); // ConnectionId => WebSocket
        private readonly TrakHoundStreamMetricsCollector _metrics;
        private readonly TrakHoundHttpEventStreamManager _eventStreams;
        private readonly object _lock = new object();
        private volatile bool _disposing;


        public TrakHoundHttpWebSocketManager(TrakHoundStreamMetricsCollector metrics = null)
        {
            _metrics = metrics;
            _eventStreams = new TrakHoundHttpEventStreamManager(metrics);
        }


        public async ValueTask DisposeAsync()
        {
            _disposing = true;

            await _eventStreams.DisposeAsync();

            List<WebSocket> webSockets;
            lock (_lock)
            {
                webSockets = _webSockets.Values.ToList();
                _webSockets.Clear();
            }

            if (!webSockets.IsNullOrEmpty())
            {
                // Close in parallel with a timeout so an unresponsive client can't stall shutdown
                using (var timeout = new CancellationTokenSource(CloseTimeout))
                {
                    await Task.WhenAll(webSockets.Select(o => CloseOnDispose(o, timeout.Token)));
                }
            }
        }

        private async Task CloseOnDispose(WebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "TrakHound WebSocketManager Closed", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error Closing WebSocket Connection : {ex.Message}");
            }
            finally
            {
                webSocket.Dispose();
            }
        }


        /// <summary>
        /// Accept a WebSocket and connect it to a TrakHound Stream. Stream output is sent to the client (formatted with outputFormatResponse)
        /// and messages from the client are pushed to the Stream (parsed with inputFormatResponse, or ignored when it is null).
        /// A Server-Sent Events Request receives the same Stream output (without input)
        /// </summary>
        public async Task Create<T>(HttpContext httpContext, Func<Task<ITrakHoundStream<T>>> getStream, Func<byte[], T> inputFormatResponse, Func<T, byte[]> outputFormatResponse)
        {
            if (!httpContext.WebSockets.IsWebSocketRequest)
            {
                if (TrakHoundHttpEventStreamManager.IsEventStreamRequest(httpContext))
                {
                    await _eventStreams.Create(httpContext, getStream, outputFormatResponse);
                    return;
                }

                _metrics?.Rejected();
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            Func<ITrakHoundStream<T>, byte[], Task> onInput = null;
            if (inputFormatResponse != null)
            {
                onInput = async (stream, message) =>
                {
                    var input = inputFormatResponse(message);
                    if (!EqualityComparer<T>.Default.Equals(input, default)) await stream.Push(input);
                };
            }

            await Run(httpContext, getStream, onInput, outputFormatResponse);
        }

        /// <summary>
        /// Accept a WebSocket whose input is handled by onInput (ex. when the input is a different type than the Stream's output). Stream output is sent to the client
        /// (formatted with outputFormatResponse). Only WebSocket Requests are accepted, since Server-Sent Events can't carry input
        /// </summary>
        public async Task CreateWithInput<T>(HttpContext httpContext, Func<Task<ITrakHoundStream<T>>> getStream, Func<ITrakHoundStream<T>, byte[], Task> onInput, Func<T, byte[]> outputFormatResponse)
        {
            if (!httpContext.WebSockets.IsWebSocketRequest)
            {
                _metrics?.Rejected();
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await Run(httpContext, getStream, onInput, outputFormatResponse);
        }

        private async Task Run<T>(HttpContext httpContext, Func<Task<ITrakHoundStream<T>>> getStream, Func<ITrakHoundStream<T>, byte[], Task> onInput, Func<T, byte[]> outputFormatResponse)
        {
            var connection = $"{httpContext.Connection.RemoteIpAddress} : {httpContext.Request.Path}{httpContext.Request.QueryString}";
            _logger.LogDebug($"WebSocket Request Received : {connection}");

            // Get the TrakHound Stream before accepting the WebSocket so a missing Stream can still return 404
            ITrakHoundStream<T> stream = null;
            try
            {
                stream = await getStream();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"WebSocket Stream Error : {connection} : {ex.Message}");
            }

            if (stream == null || string.IsNullOrEmpty(stream.Id))
            {
                stream?.Dispose();
                _metrics?.Rejected();
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var cancellationToken = httpContext.RequestAborted;
            var sendLock = new SemaphoreSlim(1, 1); // WebSocket allows only one send (or close) at a time
            WebSocket webSocket = null;
            TrakHoundStreamMetricsCollector.Connection metricsConnection = null;
            string closeReason = null;

            try
            {
                webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
                _logger.LogDebug($"WebSocket Connected : {connection}");

                lock (_lock) _webSockets[connectionId] = webSocket;

                metricsConnection = _metrics?.Opened(GetEndpointType(httpContext), GetEndpointRoute(httpContext), httpContext.Request.Path, GetRemoteAddress(httpContext), TrakHoundStreamMetricsCollector.WebSocketTransport);

                // Send Stream output to the client (after any callback the Stream already has)
                var streamOnReceivedAsync = stream.OnReceivedAsync;
                stream.OnReceivedAsync = async (o) =>
                {
                    if (streamOnReceivedAsync != null) await streamOnReceivedAsync(o);
                    await Send(webSocket, sendLock, o, outputFormatResponse, connection, metricsConnection, cancellationToken);
                };

                stream.Start();

                closeReason = await ReceiveInput(webSocket, sendLock, stream, onInput, connection, metricsConnection, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                closeReason = TrakHoundStreamMetricsCollector.DisconnectedReason;
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                // The client went away without a Close frame (ex. the browser tab was closed or the network dropped)
                closeReason = TrakHoundStreamMetricsCollector.DisconnectedReason;
            }
            catch (Exception ex)
            {
                closeReason = TrakHoundStreamMetricsCollector.ErrorReason;
                _logger.LogDebug($"WebSocket Error : {connection} : {ex.Message}");
            }
            finally
            {
                lock (_lock) _webSockets.Remove(connectionId);
                stream.Dispose();
                if (webSocket != null) webSocket.Dispose();

                _metrics?.Closed(metricsConnection, _disposing ? TrakHoundStreamMetricsCollector.ShutdownReason : closeReason);

                _logger.LogDebug($"WebSocket Disconnected : {connection}");
            }
        }


        /// <summary>
        /// The kind of Stream, from the System API prefix (or Api for an API endpoint)
        /// </summary>
        internal static string GetEndpointType(HttpContext httpContext)
        {
            var path = httpContext.Request.Path.Value?.TrimStart('/') ?? "";

            if (path.StartsWith(TrakHoundHttpConstants.WorkersPrefix, StringComparison.OrdinalIgnoreCase)) return "Workers";
            if (path.StartsWith(TrakHoundHttpConstants.FunctionsPrefix, StringComparison.OrdinalIgnoreCase)) return "Functions";
            if (path.StartsWith(TrakHoundHttpConstants.LogsPrefix, StringComparison.OrdinalIgnoreCase)) return "Logs";
            if (path.StartsWith(TrakHoundHttpConstants.MessageQueueServicePrefix, StringComparison.OrdinalIgnoreCase)) return "MessageQueues";
            if (path.StartsWith(TrakHoundHttpConstants.MessageServicePrefix, StringComparison.OrdinalIgnoreCase)) return "Messages";
            return "Api";
        }

        /// <summary>
        /// The Route template (ex. _workers/{workerId}/status/subscribe) so connections to the same endpoint are grouped (the Path when there is no template)
        /// </summary>
        internal static string GetEndpointRoute(HttpContext httpContext)
        {
            var routePattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern?.RawText;
            return !string.IsNullOrEmpty(routePattern) ? routePattern.TrimStart('/') : httpContext.Request.Path.Value?.TrimStart('/');
        }

        internal static string GetRemoteAddress(HttpContext httpContext)
        {
            var address = httpContext.Connection.RemoteIpAddress;
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }


        /// <summary>
        /// Receive messages until the WebSocket is closed (returns why it closed)
        /// </summary>
        private async Task<string> ReceiveInput<T>(WebSocket webSocket, SemaphoreSlim sendLock, ITrakHoundStream<T> stream, Func<ITrakHoundStream<T>, byte[], Task> onInput, string connection, TrakHoundStreamMetricsCollector.Connection metricsConnection, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Close(webSocket, sendLock, result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cancellationToken);
                        return TrakHoundStreamMetricsCollector.ClientClosedReason;
                    }

                    if (message.Length + result.Count > MaxInputMessageSize)
                    {
                        _logger.LogDebug($"WebSocket Message Too Big : {connection}");
                        await Close(webSocket, sendLock, WebSocketCloseStatus.MessageTooBig, null, cancellationToken);
                        return TrakHoundStreamMetricsCollector.MessageTooBigReason;
                    }

                    message.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        _metrics?.Received(metricsConnection, message.Length);

                        // Input is ignored when the endpoint has no input handler
                        if (onInput != null)
                        {
                            try
                            {
                                await onInput(stream, message.ToArray());
                            }
                            catch (Exception ex)
                            {
                                // A bad message doesn't close the WebSocket
                                _metrics?.Error(metricsConnection);
                                _logger.LogDebug($"WebSocket Input Error : {connection} : {ex.Message}");
                            }
                        }

                        // Reset (Seek alone keeps the previous Length, which appended stale bytes to shorter messages)
                        message.SetLength(0);
                    }
                }
            }
        }

        private async Task Send<T>(WebSocket webSocket, SemaphoreSlim sendLock, T message, Func<T, byte[]> formatResponse, string connection, TrakHoundStreamMetricsCollector.Connection metricsConnection, CancellationToken cancellationToken)
        {
            try
            {
                var responseBytes = formatResponse != null ? formatResponse(message) : null;
                if (responseBytes != null)
                {
                    await sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (webSocket.State == WebSocketState.Open)
                        {
                            await webSocket.SendAsync(responseBytes, WebSocketMessageType.Binary, true, cancellationToken);
                            _metrics?.Sent(metricsConnection, responseBytes.Length);
                        }
                    }
                    finally
                    {
                        sendLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _metrics?.Error(metricsConnection);
                _logger.LogDebug($"WebSocket Send Error : {connection} : {ex.Message}");
            }
        }

        /// <summary>
        /// Send a Close frame without waiting for the client's reply (the connection ends right after, so a client that never replies can't hold it open)
        /// </summary>
        private static async Task Close(WebSocket webSocket, SemaphoreSlim sendLock, WebSocketCloseStatus closeStatus, string closeStatusDescription, CancellationToken cancellationToken)
        {
            await sendLock.WaitAsync(cancellationToken);
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseOutputAsync(closeStatus, closeStatusDescription, cancellationToken);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }
    }
}
