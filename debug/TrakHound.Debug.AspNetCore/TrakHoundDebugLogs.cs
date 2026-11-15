// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using TrakHound.Logging;

namespace TrakHound.Debug.AspNetCore
{
    /// <summary>
    /// The Log entries of the debug host (the Controllers' Log entries and each Request), written to the Console and sent to the
    /// Api Debugger's Log pane as Server-Sent Events. The latest entries are kept so a page that connects later still shows them
    /// </summary>
    internal static class TrakHoundDebugLogs
    {
        private const int HistorySize = 500;

        private static readonly object _lock = new object();
        private static readonly Queue<LogEntry> _history = new Queue<LogEntry>();
        private static readonly List<Channel<LogEntry>> _subscribers = new List<Channel<LogEntry>>();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };


        public static void Write(string source, TrakHoundLogItem item)
        {
            Console.WriteLine($"Log : {item.Timestamp.ToLocalDateTime()} : {item.LogLevel} : {item.Code} : {item.Message}");
            Add(new LogEntry(item.Timestamp.ToDateTime(), item.LogLevel.ToString(), source, item.Code, item.Message));
        }

        public static void WriteRequest(string method, string path, int statusCode, double milliseconds)
        {
            var level = statusCode >= 500 ? TrakHoundLogLevel.Error : statusCode >= 400 ? TrakHoundLogLevel.Warning : TrakHoundLogLevel.Information;
            Add(new LogEntry(DateTime.UtcNow, level.ToString(), "Request", null, $"{method} {path} : {statusCode} ({milliseconds:0} ms)"));
        }

        public static void WriteError(string source, string message)
        {
            Console.WriteLine($"{source} : {message}");
            Add(new LogEntry(DateTime.UtcNow, TrakHoundLogLevel.Error.ToString(), source, null, message));
        }

        /// <summary>
        /// Sends the kept entries and then each new entry until the client disconnects or the application stops
        /// (the host would otherwise wait for the open connection until its ShutdownTimeout)
        /// </summary>
        public static async Task Stream(HttpContext httpContext, CancellationToken applicationStopping)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, applicationStopping);
            var cancellationToken = cancellation.Token;

            var response = httpContext.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(HistorySize) { FullMode = BoundedChannelFullMode.DropOldest });
            List<LogEntry> history;
            lock (_lock)
            {
                history = _history.ToList();
                _subscribers.Add(channel);
            }

            try
            {
                foreach (var entry in history) await WriteEvent(response, entry, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);

                await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await WriteEvent(response, entry, cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_lock) _subscribers.Remove(channel);
            }
        }


        private static void Add(LogEntry entry)
        {
            lock (_lock)
            {
                _history.Enqueue(entry);
                while (_history.Count > HistorySize) _history.Dequeue();

                foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(entry);
            }
        }

        private static Task WriteEvent(HttpResponse response, LogEntry entry, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            return response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), cancellationToken).AsTask();
        }


        private record LogEntry(
            [property: JsonPropertyName("timestamp")] DateTime Timestamp,
            [property: JsonPropertyName("level")] string Level,
            [property: JsonPropertyName("source")] string Source,
            [property: JsonPropertyName("code")] string Code,
            [property: JsonPropertyName("message")] string Message
            );
    }
}
