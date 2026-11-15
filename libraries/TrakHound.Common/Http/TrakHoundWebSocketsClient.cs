// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Http
{
    /// <summary>
    /// A message received by a TrakHoundWebSocketsClient
    /// </summary>
    public struct TrakHoundWebSocketResponse
    {
        /// <summary>
        /// The bytes of the message (decompressed when AcceptEncoding includes gzip)
        /// </summary>
        public byte[] Content { get; set; }
    }


    /// <summary>
    /// WebSocket Client that connects to a Url, optionally sends a Request Body after connecting, and raises ResponseReceived for each message received.
    /// Reconnects automatically (after ReconnectInterval) until stopped
    /// </summary>
    public class TrakHoundWebSocketsClient : IDisposable
    {
        private readonly string _url;
        private readonly Stream _requestBody;
        private CancellationTokenSource _stop;
        private bool _started;
        private ClientWebSocket _client;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        // Size of each ReceiveAsync read. Messages larger than this are accumulated across reads
        private const int ReceiveBufferSize = 16384;
        // A message buffer that grew past this is not kept for the next message
        private const int MaxRetainedMessageBufferSize = 1048576;


        /// <summary>
        /// Raised for each message received from the server
        /// </summary>
        public event EventHandler<TrakHoundWebSocketResponse> ResponseReceived;
        /// <summary>
        /// Raised each time the WebSocket connects (including reconnects)
        /// </summary>
        public event EventHandler Connected;
        /// <summary>
        /// Raised each time the WebSocket disconnects
        /// </summary>
        public event EventHandler Disconnected;
        /// <summary>
        /// Raised when an error occurs connecting, receiving or sending
        /// </summary>
        public event EventHandler<Exception> ConnectionError;


        /// <summary>
        /// The time (in milliseconds) to wait before reconnecting (default 10000)
        /// </summary>
        public int ReconnectInterval { get; set; } = 10000;

        /// <summary>
        /// The encodings the received messages are in ("gzip" causes messages to be decompressed)
        /// </summary>
        public IEnumerable<string> AcceptEncoding { get; set; }

        /// <summary>
        /// Headers sent with the WebSocket connection Request (ex. an API Key or Module Token)
        /// </summary>
        public IDictionary<string, string> Headers { get; set; }


        /// <summary>
        /// Creates a Client for the specified WebSocket Url
        /// </summary>
        public TrakHoundWebSocketsClient(string url)
        {
            _url = url;
        }

        /// <summary>
        /// Creates a Client for the specified WebSocket Url that sends the Request Body (serialized as JSON) after connecting
        /// </summary>
        public TrakHoundWebSocketsClient(string url, object requestBody)
        {
            _url = url;
            _requestBody = GetRequestBodyStream(requestBody);
        }

        /// <summary>
        /// Creates a Client for the specified WebSocket Url that sends the Request Body bytes after connecting
        /// </summary>
        public TrakHoundWebSocketsClient(string url, byte[] requestBody)
        {
            _url = url;
            _requestBody = GetRequestBodyStream(requestBody);
        }

        /// <summary>
        /// Creates a Client for the specified WebSocket Url that sends the contents of the Request Body Stream after connecting
        /// </summary>
        public TrakHoundWebSocketsClient(string url, Stream requestBody)
        {
            _url = url;
            _requestBody = requestBody;
        }

        /// <summary>
        /// Stops the Client and disposes the underlying WebSocket
        /// </summary>
        public void Dispose()
        {
            Stop();
            if (_client != null) _client.Dispose();
        }


        /// <summary>
        /// Starts connecting in the background (does nothing if already started)
        /// </summary>
        public void Start()
        {
            if (!_started)
            {
                _started = true;
                _stop = new CancellationTokenSource();
                _ = Task.Run(Worker, _stop.Token);
            }
        }

        /// <summary>
        /// Stops the Client and closes the WebSocket connection
        /// </summary>
        public void Stop()
        {
            if (_started)
            {
                if (_stop != null) _stop.Cancel();

                try
                {
                    if (_client != null)
                    {
                        _client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }
                }
                catch { }

                _started = false;
            }
        }


        /// <summary>
        /// Sends a binary message to the server. Returns false when it wasn't sent (ex. the WebSocket is not open yet)
        /// </summary>
        public async Task<bool> Send(ArraySegment<byte> messageBody)
        {
            try
            {
                var client = _client;
                if (client != null && client.State == WebSocketState.Open)
                {
                    // ClientWebSocket doesn't allow concurrent SendAsync calls
                    await _sendLock.WaitAsync(_stop.Token);
                    try
                    {
                        await client.SendAsync(messageBody, WebSocketMessageType.Binary, true, _stop.Token);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                if (ConnectionError != null) ConnectionError.Invoke(this, ex);
            }

            return false;
        }



        private async Task Worker()
        {
            do
            {
                try
                {
                    try
                    {
                        _client = new ClientWebSocket();

                        if (!Headers.IsNullOrEmpty())
                        {
                            foreach (var header in Headers) _client.Options.SetRequestHeader(header.Key, header.Value);
                        }

                        // Ignore Certificate Validation Errors (probably shouldn't be used in production)
                        _client.Options.RemoteCertificateValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) =>
                        {
                            return true; // Allow Untrusted Server Certificate
                        };

                        await _client.ConnectAsync(new Uri(_url), _stop.Token);

                        if (Connected != null) Connected.Invoke(this, new EventArgs());


                        // Send Request Body
                        if (_requestBody != null)
                        {
                            byte[] requestBodyBytes;
                            using (var requestBodyStream = new MemoryStream())
                            {
                                await _requestBody.CopyToAsync(requestBodyStream);

                                requestBodyBytes = requestBodyStream.ToArray();
                            }

                            // Serialized with Send (ClientWebSocket doesn't allow concurrent SendAsync calls)
                            await _sendLock.WaitAsync(_stop.Token);
                            try
                            {
                                await _client.SendAsync(requestBodyBytes, WebSocketMessageType.Binary, true, _stop.Token);
                            }
                            finally
                            {
                                _sendLock.Release();
                            }
                        }


                        // Receive into a pooled buffer. A message that spans multiple reads is accumulated in a MemoryStream (which doubles as it grows)
                        var receiveBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
                        var messageBuffer = new MemoryStream();
                        try
                        {
                            while (_client.State == WebSocketState.Open)
                            {
                                WebSocketReceiveResult result = null;
                                byte[] responseBytes = null;
                                messageBuffer.SetLength(0);

                                while (_client.State == WebSocketState.Open)
                                {
                                    result = await _client.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), _stop.Token);

                                    if (result.EndOfMessage && messageBuffer.Length == 0)
                                    {
                                        // Single read message (the common case): copy straight to an exactly sized array
                                        responseBytes = new byte[result.Count];
                                        Buffer.BlockCopy(receiveBuffer, 0, responseBytes, 0, result.Count);
                                        break;
                                    }

                                    messageBuffer.Write(receiveBuffer, 0, result.Count);

                                    if (result.EndOfMessage)
                                    {
                                        responseBytes = messageBuffer.ToArray();
                                        break;
                                    }
                                }

                                // Only complete messages are handled
                                if (responseBytes != null) HandleResponse(result, responseBytes);

                                if (messageBuffer.Capacity > MaxRetainedMessageBufferSize)
                                {
                                    messageBuffer.Dispose();
                                    messageBuffer = new MemoryStream();
                                }
                            }
                        }
                        finally
                        {
                            messageBuffer.Dispose();
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                        }
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        if (ConnectionError != null) ConnectionError.Invoke(this, ex);
                    }

                    // Only close a socket that is still open (a failed ConnectAsync leaves it closed and disposed)
                    if (_client != null)
                    {
                        if (_client.State == WebSocketState.Open || _client.State == WebSocketState.CloseReceived)
                        {
                            try { await _client.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, null, _stop.Token); }
                            catch { }
                        }

                        _client.Dispose();
                    }

                    if (Disconnected != null) Disconnected.Invoke(this, new EventArgs());
                }
                catch (TaskCanceledException) { }
                catch (Exception) { }

                // Always wait before reconnecting. Skipping this when the close above threw
                // caused a tight reconnect loop for any route that fails to connect
                try { await Task.Delay(ReconnectInterval, _stop.Token); }
                catch (TaskCanceledException) { }

            } while (!_stop.Token.IsCancellationRequested);
        }

        private void HandleResponse(WebSocketReceiveResult result, byte[] responseBytes)
        {
            if (result != null && !responseBytes.IsNullOrEmpty())
            {
                try
                {
                    if (AcceptEncoding != null && AcceptEncoding.Contains("gzip"))
                    {
                        using (var inputStream = new MemoryStream(responseBytes))
                        using (var outputStream = new MemoryStream())
                        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                        {
                            gzipStream.CopyTo(outputStream);

                            var bytes = outputStream.ToArray();
                            if (!bytes.IsNullOrEmpty())
                            {
                                var response = new TrakHoundWebSocketResponse();
                                response.Content = bytes;

                                if (ResponseReceived != null) ResponseReceived.Invoke(this, response);
                            }
                        }
                    }
                    else
                    {
                        if (!responseBytes.IsNullOrEmpty())
                        {
                            var response = new TrakHoundWebSocketResponse();
                            response.Content = responseBytes;

                            if (ResponseReceived != null) ResponseReceived.Invoke(this, response);
                        }
                    }
                }
                catch { }
            }
        }


        private static Stream GetRequestBodyStream(object requestBody)
        {
            if (requestBody != null)
            {
                var jsonRequest = Json.Convert(requestBody);
                if (!string.IsNullOrEmpty(jsonRequest))
                {
                    return GetRequestBodyStream(jsonRequest.ToUtf8Bytes());
                }
            }

            return null;
        }

        private static Stream GetRequestBodyStream(byte[] requestBody)
        {
            if (!requestBody.IsNullOrEmpty())
            {
                try
                {
                    var stream = new MemoryStream(requestBody);
                    stream.Seek(0, SeekOrigin.Begin);
                    return stream;
                }
                catch { }
            }

            return null;
        }
    }
}
