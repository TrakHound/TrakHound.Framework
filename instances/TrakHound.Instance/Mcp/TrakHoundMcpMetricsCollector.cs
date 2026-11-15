// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Mcp;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// Collects Metrics for the MCP Server (Tool calls and the Clients that connect)
    /// </summary>
    public class TrakHoundMcpMetricsCollector
    {
        /// <summary>
        /// The window (in seconds) that Rates, Average and Max are calculated over
        /// </summary>
        public const int MetricsWindow = 10;

        /// <summary>
        /// Clients are grouped by name, version and address, so new Clients past this limit are not added
        /// </summary>
        private const int MaxClients = 500;

        private readonly Dictionary<string, ToolState> _tools = new(StringComparer.OrdinalIgnoreCase); // Tool Name => Tool
        private readonly Dictionary<string, TrakHoundMcpClientMetrics> _clients = new(); // Name:Version:Address => Client
        private readonly SecondBucket[] _buckets = Enumerable.Range(0, MetricsWindow + 1).Select(o => new SecondBucket { Second = -1 }).ToArray(); // One extra for the current (incomplete) second
        private readonly object _lock = new();

        private long _totalCalls;
        private long _totalErrors;
        private long _totalDenied;
        private long _totalInitializes;
        private DateTime? _lastCalled;


        /// <summary>
        /// Collect Metrics (set from the Instance's Metrics settings when the Runtime is created)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;


        class ToolState
        {
            public string Name;
            public long Calls;
            public long Errors;
            public long Denied;
            public double DurationSum;
            public double DurationMax;
            public string LastError;
            public DateTime? LastCalled;
        }

        // Totals for one second (a ring of these makes up the Metrics window)
        class SecondBucket
        {
            public long Second;
            public long Calls;
            public long Errors;
            public double DurationSum;
            public double DurationMax;

            public void Reset(long second)
            {
                Second = second;
                Calls = 0;
                Errors = 0;
                DurationSum = 0;
                DurationMax = 0;
            }
        }


        /// <summary>
        /// Record a Tool call. Error is the message of a call that failed (null when it succeeded)
        /// </summary>
        public void ToolCalled(string name, double duration, string error, bool denied)
        {
            if (!MetricsEnabled || string.IsNullOrEmpty(name)) return;

            var now = DateTime.UtcNow;
            var failed = error != null || denied;

            lock (_lock)
            {
                if (!_tools.TryGetValue(name, out var tool))
                {
                    tool = new ToolState { Name = name };
                    _tools[name] = tool;
                }

                tool.Calls++;
                tool.DurationSum += duration;
                tool.DurationMax = Math.Max(tool.DurationMax, duration);
                tool.LastCalled = now;
                if (failed)
                {
                    tool.Errors++;
                    tool.LastError = error;
                }
                if (denied) tool.Denied++;

                _totalCalls++;
                if (failed) _totalErrors++;
                if (denied) _totalDenied++;
                _lastCalled = now;

                var bucket = GetBucket(now);
                bucket.Calls++;
                if (failed) bucket.Errors++;
                bucket.DurationSum += duration;
                bucket.DurationMax = Math.Max(bucket.DurationMax, duration);
            }
        }

        /// <summary>
        /// Record a Client connecting (an MCP initialize request)
        /// </summary>
        public void ClientInitialized(string name, string version, string remoteAddress)
        {
            if (!MetricsEnabled) return;

            var key = $"{name}:{version}:{remoteAddress}";
            lock (_lock)
            {
                _totalInitializes++;

                if (!_clients.TryGetValue(key, out var client))
                {
                    if (_clients.Count >= MaxClients) return;

                    client = new TrakHoundMcpClientMetrics { Name = name, Version = version, RemoteAddress = remoteAddress };
                    _clients[key] = client;
                }

                client.InitializeCount++;
                client.LastConnected = DateTime.UtcNow;
            }
        }


        public TrakHoundMcpMetrics GetMetrics()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                var metrics = new TrakHoundMcpMetrics();
                metrics.Window = MetricsWindow;
                metrics.TotalCallCount = _totalCalls;
                metrics.TotalErrorCount = _totalErrors;
                metrics.TotalDeniedCount = _totalDenied;
                metrics.TotalInitializeCount = _totalInitializes;
                metrics.LastCalled = _lastCalled;

                // Window values use complete seconds only (the current second is still filling)
                long calls = 0, errors = 0;
                double durationSum = 0, durationMax = 0;
                foreach (var bucket in _buckets)
                {
                    if (bucket.Second >= now - MetricsWindow && bucket.Second < now)
                    {
                        calls += bucket.Calls;
                        errors += bucket.Errors;
                        durationSum += bucket.DurationSum;
                        durationMax = Math.Max(durationMax, bucket.DurationMax);
                    }
                }

                metrics.CallRate = Math.Round((double)calls / MetricsWindow, 4);
                metrics.ErrorRate = Math.Round((double)errors / MetricsWindow, 4);
                metrics.AverageDuration = calls > 0 ? Math.Round(durationSum / calls, 4) : 0;
                metrics.MaxDuration = Math.Round(durationMax, 4);

                metrics.Tools = _tools.Values
                    .Select(o => new TrakHoundMcpToolMetrics
                    {
                        Name = o.Name,
                        TotalCallCount = o.Calls,
                        TotalErrorCount = o.Errors,
                        TotalDeniedCount = o.Denied,
                        AverageDuration = o.Calls > 0 ? Math.Round(o.DurationSum / o.Calls, 4) : 0,
                        MaxDuration = Math.Round(o.DurationMax, 4),
                        LastError = o.LastError,
                        LastCalled = o.LastCalled
                    })
                    .OrderByDescending(o => o.TotalCallCount)
                    .ToList();

                metrics.Clients = _clients.Values
                    .Select(o => new TrakHoundMcpClientMetrics
                    {
                        Name = o.Name,
                        Version = o.Version,
                        RemoteAddress = o.RemoteAddress,
                        InitializeCount = o.InitializeCount,
                        LastConnected = o.LastConnected
                    })
                    .OrderByDescending(o => o.LastConnected)
                    .ToList();

                return metrics;
            }
        }


        // Call within _lock
        private SecondBucket GetBucket(DateTime timestamp)
        {
            var second = timestamp.ToUnixTimeSeconds();
            var bucket = _buckets[second % _buckets.Length];
            if (bucket.Second != second) bucket.Reset(second);
            return bucket;
        }
    }
}
