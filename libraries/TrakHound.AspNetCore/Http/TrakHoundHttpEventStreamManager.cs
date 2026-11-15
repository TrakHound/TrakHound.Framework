// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http.Features;
using System.Text;
using System.Text.Unicode;
using TrakHound.Logging;

namespace TrakHound.Http
{
    /// <summary>
    /// Connects Server-Sent Events (text/event-stream) Requests to TrakHound Streams. Each Stream output is sent to the client as a "data" event.
    /// The client can't send messages back (unlike a WebSocket), so Stream input isn't available
    /// </summary>
    public class TrakHoundHttpEventStreamManager : IAsyncDisposable
    {
        public const string ContentType = "text/event-stream";

        /// <summary>
        /// How often a comment is sent on an idle connection (so proxies and clients don't time it out). Matches the WebSocket KeepAliveInterval
        /// </summary>
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

        private static readonly byte[] ConnectedBytes = Encoding.UTF8.GetBytes(": connected\n\n");
        private static readonly byte[] KeepAliveBytes = Encoding.UTF8.GetBytes(": keep-alive\n\n");

        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundHttpEventStreamManager>();
        private readonly Dictionary<string, CancellationTokenSource> _connections = new Dictionary<string, CancellationTokenSource>(); // ConnectionId => Connection Cancellation
        private readonly TrakHoundStreamMetricsCollector _metrics;
        private readonly object _lock = new object();
        private volatile bool _disposing;


        public TrakHoundHttpEventStreamManager(TrakHoundStreamMetricsCollector metrics = null)
        {
            _metrics = metrics;
        }


        /// <summary>
        /// End every open connection (the Requests complete on their own once cancelled)
        /// </summary>
        public ValueTask DisposeAsync()
        {
            _disposing = true;

            List<CancellationTokenSource> connections;
            lock (_lock)
            {
                connections = _connections.Values.ToList();
                _connections.Clear();
            }

            foreach (var connection in connections)
            {
                try { connection.Cancel(); }
                catch (ObjectDisposedException) { } // The connection ended at the same time
            }

            return ValueTask.CompletedTask;
        }


        /// <summary>
        /// A GET Request that accepts text/event-stream (ex. a browser EventSource, or curl -H "Accept: text/event-stream")
        /// </summary>
        public static bool IsEventStreamRequest(HttpContext httpContext)
        {
            if (!HttpMethods.IsGet(httpContext.Request.Method)) return false;

            var accept = httpContext.Request.GetTypedHeaders().Accept;
            if (accept.IsNullOrEmpty()) return false;

            return accept.Any(o => o.MediaType.Equals(ContentType, StringComparison.OrdinalIgnoreCase));
        }


        /// <summary>
        /// Respond with an Event Stream connected to a TrakHound Stream. Stream output is sent to the client (formatted with outputFormatResponse)
        /// until the client disconnects or the Instance shuts down
        /// </summary>
        public async Task Create<T>(HttpContext httpContext, Func<Task<ITrakHoundStream<T>>> getStream, Func<T, byte[]> outputFormatResponse)
        {
            var connection = $"{httpContext.Connection.RemoteIpAddress} : {httpContext.Request.Path}{httpContext.Request.QueryString}";
            _logger.LogDebug($"Event Stream Request Received : {connection}");

            // Get the TrakHound Stream before starting the Response so a missing Stream can still return 404
            ITrakHoundStream<T> stream = null;
            try
            {
                stream = await getStream();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Event Stream Error : {connection} : {ex.Message}");
            }

            if (stream == null || string.IsNullOrEmpty(stream.Id))
            {
                stream?.Dispose();
                _metrics?.Rejected();
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
            var cancellationToken = cancellation.Token;
            var writeLock = new SemaphoreSlim(1, 1); // Stream output and Keep Alive comments can't be written at the same time
            TrakHoundStreamMetricsCollector.Connection metricsConnection = null;
            string closeReason = null;

            lock (_lock) _connections[connectionId] = cancellation;
            if (_disposing) cancellation.Cancel();

            try
            {
                var response = httpContext.Response;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = ContentType;
                response.Headers.CacheControl = "no-cache";
                response.Headers["X-Accel-Buffering"] = "no"; // Tell proxies (ex. nginx) not to buffer the Response

                // Send each event as soon as it's written
                httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                // Send a comment right away so the client (and any proxy) sees the Stream is open
                await Write(response, writeLock, ConnectedBytes, cancellationToken);
                _logger.LogDebug($"Event Stream Connected : {connection}");

                metricsConnection = _metrics?.Opened(TrakHoundHttpWebSocketManager.GetEndpointType(httpContext), TrakHoundHttpWebSocketManager.GetEndpointRoute(httpContext), httpContext.Request.Path, TrakHoundHttpWebSocketManager.GetRemoteAddress(httpContext), TrakHoundStreamMetricsCollector.EventStreamTransport);

                // Send Stream output to the client (after any callback the Stream already has)
                var streamOnReceivedAsync = stream.OnReceivedAsync;
                stream.OnReceivedAsync = async (o) =>
                {
                    if (streamOnReceivedAsync != null) await streamOnReceivedAsync(o);
                    await Send(response, writeLock, o, outputFormatResponse, connection, metricsConnection, cancellationToken);
                };

                stream.Start();

                // Hold the connection open until it's cancelled
                while (true)
                {
                    await Task.Delay(KeepAliveInterval, cancellationToken);
                    await Write(response, writeLock, KeepAliveBytes, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                closeReason = TrakHoundStreamMetricsCollector.DisconnectedReason;
            }
            catch (Exception ex) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client went away during a write
                closeReason = TrakHoundStreamMetricsCollector.DisconnectedReason;
                _logger.LogDebug($"Event Stream Write Aborted : {connection} : {ex.Message}");
            }
            catch (Exception ex)
            {
                closeReason = TrakHoundStreamMetricsCollector.ErrorReason;
                _logger.LogDebug($"Event Stream Error : {connection} : {ex.Message}");
            }
            finally
            {
                lock (_lock) _connections.Remove(connectionId);
                stream.Dispose();
                cancellation.Dispose();

                _metrics?.Closed(metricsConnection, _disposing ? TrakHoundStreamMetricsCollector.ShutdownReason : closeReason);

                _logger.LogDebug($"Event Stream Disconnected : {connection}");
            }
        }


        private async Task Send<T>(HttpResponse response, SemaphoreSlim writeLock, T message, Func<T, byte[]> formatResponse, string connection, TrakHoundStreamMetricsCollector.Connection metricsConnection, CancellationToken cancellationToken)
        {
            try
            {
                var responseBytes = formatResponse != null ? formatResponse(message) : null;
                if (responseBytes != null)
                {
                    await Write(response, writeLock, FormatEvent(responseBytes), cancellationToken);
                    _metrics?.Sent(metricsConnection, responseBytes.Length);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _metrics?.Error(metricsConnection);
                _logger.LogDebug($"Event Stream Send Error : {connection} : {ex.Message}");
            }
        }

        private static async Task Write(HttpResponse response, SemaphoreSlim writeLock, byte[] bytes, CancellationToken cancellationToken)
        {
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await response.Body.WriteAsync(bytes, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
            finally
            {
                writeLock.Release();
            }
        }

        /// <summary>
        /// An event with a "data" field for each line (clients join them back together with newlines)
        /// </summary>
        internal static byte[] FormatEvent(byte[] data)
        {
            // Compact JSON (the usual payload) has no line breaks, so it's copied into one buffer without decoding.
            // Invalid UTF-8 still goes through the text path so it's replaced the same way
            if (data.AsSpan().IndexOfAny((byte)'\r', (byte)'\n') < 0 && Utf8.IsValid(data))
            {
                var prefix = "data: "u8;
                var result = new byte[prefix.Length + data.Length + 2];
                prefix.CopyTo(result);
                data.CopyTo(result, prefix.Length);
                result[result.Length - 2] = (byte)'\n';
                result[result.Length - 1] = (byte)'\n';
                return result;
            }

            var text = Encoding.UTF8.GetString(data);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var builder = new StringBuilder(text.Length + lines.Length * 7 + 1);
            foreach (var line in lines)
            {
                builder.Append("data: ");
                builder.Append(line);
                builder.Append('\n');
            }
            builder.Append('\n');

            return Encoding.UTF8.GetBytes(builder.ToString());
        }
    }
}
