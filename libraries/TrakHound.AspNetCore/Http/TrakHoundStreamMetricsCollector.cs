// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Http
{
    /// <summary>
    /// Collects Metrics for the HTTP Stream connections (WebSockets and Server-Sent Events) handled by the TrakHoundHttpWebSocketManager
    /// </summary>
    public class TrakHoundStreamMetricsCollector
    {
        /// <summary>
        /// The window (in seconds) that Rates are calculated over
        /// </summary>
        public const int MetricsWindow = 10;

        /// <summary>
        /// Endpoints are grouped by Route, but a Route without a template (ex. an API path) can include IDs,
        /// so new Endpoints past this limit are counted together as "Other"
        /// </summary>
        private const int MaxEndpoints = 500;
        private const string OtherRoute = "Other";

        public const string ClientClosedReason = "Closed by Client";
        public const string DisconnectedReason = "Disconnected";
        public const string ErrorReason = "Error";
        public const string MessageTooBigReason = "Message Too Big";
        public const string ShutdownReason = "Shutdown";

        public const string WebSocketTransport = "WebSocket";
        public const string EventStreamTransport = "SSE";

        private readonly Dictionary<string, Connection> _connections = new(); // Connection ID => Connection
        private readonly Dictionary<string, EndpointState> _endpoints = new(); // Transport:Type:Route => Endpoint
        private readonly Dictionary<string, long> _closeReasons = new(); // Reason => Count
        private readonly SecondBucket[] _buckets = Enumerable.Range(0, MetricsWindow + 1).Select(o => new SecondBucket { Second = -1 }).ToArray(); // One extra for the current (incomplete) second
        private readonly object _lock = new();

        private long _totalConnections;
        private long _totalRejected;
        private long _totalMessagesSent;
        private long _totalMessagesReceived;
        private long _totalBytesSent;
        private long _totalBytesReceived;
        private long _totalErrors;
        private long _closedCount;
        private double _closedDurationSum;
        private double _closedDurationMax;
        private int _peakConnections;
        private DateTime? _lastConnected;


        /// <summary>
        /// Collect Metrics (set from the Instance's Metrics settings when the Runtime is created)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;


        /// <summary>
        /// An open WebSocket or Event Stream connection (returned by Opened and passed back for each message)
        /// </summary>
        public class Connection
        {
            internal string Id;
            internal string Transport;
            internal string Type;
            internal string Route;
            internal string Path;
            internal string RemoteAddress;
            internal DateTime Connected;
            internal EndpointState Endpoint;
            internal long MessagesSent;
            internal long MessagesReceived;
            internal long BytesSent;
            internal long BytesReceived;
            internal long Errors;
            internal long LastActivityTicks; // UTC Ticks (0 = None), since a DateTime? can't be written atomically
        }

        internal class EndpointState
        {
            public string Transport;
            public string Type;
            public string Route;
            public int Open;
            public long Connections;
            public long MessagesSent;
            public long MessagesReceived;
            public long BytesSent;
            public long BytesReceived;
            public long Errors;
            public DateTime? LastConnected;
        }

        // Totals for one second (a ring of these makes up the Metrics window)
        class SecondBucket
        {
            public long Second;
            public long Connections;
            public long Disconnections;
            public long MessagesSent;
            public long MessagesReceived;
            public long BytesSent;
            public long BytesReceived;
            public long Errors;

            // Call within _lock. The counters are cleared before the new Second is published, so a lock free add for the new Second is never cleared
            public void Reset(long second)
            {
                Interlocked.Exchange(ref Connections, 0);
                Interlocked.Exchange(ref Disconnections, 0);
                Interlocked.Exchange(ref MessagesSent, 0);
                Interlocked.Exchange(ref MessagesReceived, 0);
                Interlocked.Exchange(ref BytesSent, 0);
                Interlocked.Exchange(ref BytesReceived, 0);
                Interlocked.Exchange(ref Errors, 0);
                Volatile.Write(ref Second, second);
            }
        }


        /// <summary>
        /// Record an accepted connection (returns null when Metrics are disabled)
        /// </summary>
        public Connection Opened(string type, string route, string path, string remoteAddress, string transport = WebSocketTransport)
        {
            if (!MetricsEnabled) return null;

            var now = DateTime.UtcNow;
            var connection = new Connection
            {
                Id = Guid.NewGuid().ToString(),
                Transport = transport,
                Type = type,
                Route = route,
                Path = path,
                RemoteAddress = remoteAddress,
                Connected = now
            };

            lock (_lock)
            {
                connection.Endpoint = GetEndpoint(transport, type, route);
                connection.Endpoint.Open++;
                connection.Endpoint.Connections++;
                connection.Endpoint.LastConnected = now;

                _connections[connection.Id] = connection;
                _totalConnections++;
                _peakConnections = Math.Max(_peakConnections, _connections.Count);
                _lastConnected = now;

                Interlocked.Increment(ref GetBucket(now).Connections);
            }

            return connection;
        }

        /// <summary>
        /// Record a WebSocket or Event Stream request that was not accepted
        /// </summary>
        public void Rejected()
        {
            if (!MetricsEnabled) return;

            lock (_lock) _totalRejected++;
        }

        // Message counters are updated lock free (called for every message, so busy streams would contend on _lock)
        public void Sent(Connection connection, int bytes)
        {
            if (connection == null) return;

            var now = DateTime.UtcNow;
            Interlocked.Increment(ref connection.MessagesSent);
            Interlocked.Add(ref connection.BytesSent, bytes);
            Interlocked.Exchange(ref connection.LastActivityTicks, now.Ticks);
            Interlocked.Increment(ref connection.Endpoint.MessagesSent);
            Interlocked.Add(ref connection.Endpoint.BytesSent, bytes);
            Interlocked.Increment(ref _totalMessagesSent);
            Interlocked.Add(ref _totalBytesSent, bytes);

            var bucket = GetBucket(now);
            Interlocked.Increment(ref bucket.MessagesSent);
            Interlocked.Add(ref bucket.BytesSent, bytes);
        }

        public void Received(Connection connection, long bytes)
        {
            if (connection == null) return;

            var now = DateTime.UtcNow;
            Interlocked.Increment(ref connection.MessagesReceived);
            Interlocked.Add(ref connection.BytesReceived, bytes);
            Interlocked.Exchange(ref connection.LastActivityTicks, now.Ticks);
            Interlocked.Increment(ref connection.Endpoint.MessagesReceived);
            Interlocked.Add(ref connection.Endpoint.BytesReceived, bytes);
            Interlocked.Increment(ref _totalMessagesReceived);
            Interlocked.Add(ref _totalBytesReceived, bytes);

            var bucket = GetBucket(now);
            Interlocked.Increment(ref bucket.MessagesReceived);
            Interlocked.Add(ref bucket.BytesReceived, bytes);
        }

        /// <summary>
        /// Record a message that failed to send, or a received message that could not be read
        /// </summary>
        public void Error(Connection connection)
        {
            if (connection == null) return;

            Interlocked.Increment(ref connection.Errors);
            Interlocked.Increment(ref connection.Endpoint.Errors);
            Interlocked.Increment(ref _totalErrors);
            Interlocked.Increment(ref GetBucket(DateTime.UtcNow).Errors);
        }

        public void Closed(Connection connection, string reason)
        {
            if (connection == null) return;

            var now = DateTime.UtcNow;
            var duration = Math.Max(0, (now - connection.Connected).TotalMilliseconds);

            lock (_lock)
            {
                if (!_connections.Remove(connection.Id)) return;

                connection.Endpoint.Open--;

                _closedCount++;
                _closedDurationSum += duration;
                _closedDurationMax = Math.Max(_closedDurationMax, duration);

                reason ??= DisconnectedReason;
                _closeReasons[reason] = _closeReasons.GetValueOrDefault(reason) + 1;

                Interlocked.Increment(ref GetBucket(now).Disconnections);
            }
        }


        public TrakHoundStreamMetrics GetMetrics()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                var metrics = new TrakHoundStreamMetrics();
                metrics.Window = MetricsWindow;
                metrics.OpenConnectionCount = _connections.Count;
                metrics.PeakConnectionCount = _peakConnections;
                metrics.TotalConnectionCount = _totalConnections;
                metrics.TotalRejectedCount = _totalRejected;
                metrics.TotalMessagesSent = Interlocked.Read(ref _totalMessagesSent);
                metrics.TotalMessagesReceived = Interlocked.Read(ref _totalMessagesReceived);
                metrics.TotalBytesSent = Interlocked.Read(ref _totalBytesSent);
                metrics.TotalBytesReceived = Interlocked.Read(ref _totalBytesReceived);
                metrics.TotalErrorCount = Interlocked.Read(ref _totalErrors);
                metrics.AverageDuration = _closedCount > 0 ? Math.Round(_closedDurationSum / _closedCount, 4) : 0;
                metrics.MaxDuration = Math.Round(_closedDurationMax, 4);
                metrics.LastConnected = _lastConnected;

                // Window values use complete seconds only (the current second is still filling)
                long connections = 0, disconnections = 0, messagesSent = 0, messagesReceived = 0, bytesSent = 0, bytesReceived = 0, errors = 0;
                foreach (var bucket in _buckets)
                {
                    if (bucket.Second >= now - MetricsWindow && bucket.Second < now)
                    {
                        connections += Interlocked.Read(ref bucket.Connections);
                        disconnections += Interlocked.Read(ref bucket.Disconnections);
                        messagesSent += Interlocked.Read(ref bucket.MessagesSent);
                        messagesReceived += Interlocked.Read(ref bucket.MessagesReceived);
                        bytesSent += Interlocked.Read(ref bucket.BytesSent);
                        bytesReceived += Interlocked.Read(ref bucket.BytesReceived);
                        errors += Interlocked.Read(ref bucket.Errors);
                    }
                }

                metrics.ConnectionRate = Math.Round((double)connections / MetricsWindow, 4);
                metrics.DisconnectionRate = Math.Round((double)disconnections / MetricsWindow, 4);
                metrics.MessageSentRate = Math.Round((double)messagesSent / MetricsWindow, 4);
                metrics.MessageReceivedRate = Math.Round((double)messagesReceived / MetricsWindow, 4);
                metrics.ByteSentRate = Math.Round((double)bytesSent / MetricsWindow, 4);
                metrics.ByteReceivedRate = Math.Round((double)bytesReceived / MetricsWindow, 4);
                metrics.ErrorRate = Math.Round((double)errors / MetricsWindow, 4);

                metrics.CloseReasons = _closeReasons
                    .Select(o => new TrakHoundStreamCloseReasonMetrics { Reason = o.Key, Count = o.Value })
                    .OrderByDescending(o => o.Count)
                    .ToList();

                metrics.Endpoints = _endpoints.Values
                    .Select(o => new TrakHoundStreamEndpointMetrics
                    {
                        Transport = o.Transport,
                        Type = o.Type,
                        Route = o.Route,
                        OpenConnectionCount = o.Open,
                        TotalConnectionCount = o.Connections,
                        TotalMessagesSent = Interlocked.Read(ref o.MessagesSent),
                        TotalMessagesReceived = Interlocked.Read(ref o.MessagesReceived),
                        TotalBytesSent = Interlocked.Read(ref o.BytesSent),
                        TotalBytesReceived = Interlocked.Read(ref o.BytesReceived),
                        TotalErrorCount = Interlocked.Read(ref o.Errors),
                        LastConnected = o.LastConnected
                    })
                    .OrderByDescending(o => o.OpenConnectionCount)
                    .ThenByDescending(o => o.TotalConnectionCount)
                    .ToList();

                metrics.Connections = _connections.Values
                    .Select(o => new TrakHoundStreamConnectionMetrics
                    {
                        Id = o.Id,
                        Transport = o.Transport,
                        Type = o.Type,
                        Route = o.Route,
                        Path = o.Path,
                        RemoteAddress = o.RemoteAddress,
                        Connected = o.Connected,
                        MessagesSent = Interlocked.Read(ref o.MessagesSent),
                        MessagesReceived = Interlocked.Read(ref o.MessagesReceived),
                        BytesSent = Interlocked.Read(ref o.BytesSent),
                        BytesReceived = Interlocked.Read(ref o.BytesReceived),
                        ErrorCount = Interlocked.Read(ref o.Errors),
                        LastActivity = GetLastActivity(o)
                    })
                    .OrderBy(o => o.Connected)
                    .ToList();

                return metrics;
            }
        }


        // Call within _lock
        private EndpointState GetEndpoint(string transport, string type, string route)
        {
            var key = $"{transport}:{type}:{route}";
            if (_endpoints.TryGetValue(key, out var endpoint)) return endpoint;

            if (_endpoints.Count >= MaxEndpoints)
            {
                route = OtherRoute;
                key = $"{transport}:{type}:{route}";
                if (_endpoints.TryGetValue(key, out endpoint)) return endpoint;
            }

            endpoint = new EndpointState { Transport = transport, Type = type, Route = route };
            _endpoints.Add(key, endpoint);
            return endpoint;
        }

        private static DateTime? GetLastActivity(Connection connection)
        {
            var ticks = Interlocked.Read(ref connection.LastActivityTicks);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }

        // Lock free, except when the bucket starts a new second (once a second, so the reset is still done within _lock)
        private SecondBucket GetBucket(DateTime timestamp)
        {
            var second = timestamp.ToUnixTimeSeconds();
            var bucket = _buckets[second % _buckets.Length];
            if (Volatile.Read(ref bucket.Second) < second)
            {
                lock (_lock)
                {
                    if (bucket.Second < second) bucket.Reset(second);
                }
            }
            return bucket;
        }
    }
}
