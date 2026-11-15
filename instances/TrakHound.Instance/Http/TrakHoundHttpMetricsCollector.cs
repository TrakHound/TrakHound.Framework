// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Http
{
    /// <summary>
    /// Collects Metrics for the HTTP Requests received by the Instance (recorded by the TrakHoundHttpConnectionMiddleware)
    /// </summary>
    public class TrakHoundHttpMetricsCollector
    {
        /// <summary>
        /// The window (in seconds) that Rates, Average and Max values are calculated over
        /// </summary>
        public const int MetricsWindow = 10;

        /// <summary>
        /// Endpoints and Clients are limited (a Route without a template can include IDs, and Clients come and go),
        /// so new ones past this limit are counted together as "Other"
        /// </summary>
        private const int MaxGroups = 500;
        private const string OtherName = "Other";

        private readonly Dictionary<string, GroupState> _areas = new(); // Area => State
        private readonly Dictionary<EndpointKey, GroupState> _endpoints = new(); // Method + Route => State
        private readonly Dictionary<string, GroupState> _clients = new(); // Address => State
        private readonly Dictionary<int, long> _statusCodes = new(); // Status Code => Count
        private readonly SecondBucket[] _buckets = Enumerable.Range(0, MetricsWindow + 1).Select(o => new SecondBucket { Second = -1 }).ToArray(); // One extra for the current (incomplete) second
        private readonly object _lock = new();

        private long _totalRequests;
        private long _totalErrors;
        private long _totalServerErrors;
        private long _totalRequestBytes;
        private long _totalResponseBytes;
        private DateTime? _lastRequested;
        private int _activeRequests;
        private int _peakActiveRequests;


        /// <summary>
        /// Collect Metrics (set from the Instance's Metrics settings when the Runtime is created)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;


        // A struct key so no "Method:Route" string is built per Request (Other marks the shared group past MaxGroups)
        readonly record struct EndpointKey(string Method, string Route, bool Other);

        private static readonly EndpointKey OtherEndpointKey = new(null, null, true);


        class GroupState
        {
            public string Name;
            public string Method;
            public string Area;
            public long Requests;
            public long Errors;
            public long RequestBytes;
            public long ResponseBytes;
            public long TimedRequests;
            public double DurationSum;
            public double DurationMax;
            public int LastStatusCode;
            public DateTime? LastRequested;

            public void Add(int statusCode, long requestBytes, long responseBytes, double? duration, DateTime requested)
            {
                Requests++;
                if (statusCode >= 400) Errors++;
                RequestBytes += requestBytes;
                ResponseBytes += responseBytes;
                LastStatusCode = statusCode;
                LastRequested = requested;

                if (duration.HasValue)
                {
                    TimedRequests++;
                    DurationSum += duration.Value;
                    DurationMax = Math.Max(DurationMax, duration.Value);
                }
            }

            public TrakHoundHttpRequestGroupMetrics ToMetrics() => new TrakHoundHttpRequestGroupMetrics
            {
                Name = Name,
                Method = Method,
                Area = Area,
                TotalRequestCount = Requests,
                TotalErrorCount = Errors,
                TotalRequestBytes = RequestBytes,
                TotalResponseBytes = ResponseBytes,
                AverageDuration = TimedRequests > 0 ? Math.Round(DurationSum / TimedRequests, 4) : 0,
                MaxDuration = Math.Round(DurationMax, 4),
                LastStatusCode = LastStatusCode,
                LastRequested = LastRequested
            };
        }

        // Totals for one second (a ring of these makes up the Metrics window)
        class SecondBucket
        {
            public long Second;
            public long Requests;
            public long Errors;
            public long ServerErrors;
            public long RequestBytes;
            public long ResponseBytes;
            public long TimedRequests;
            public double DurationSum;
            public double DurationMax;

            public void Reset(long second)
            {
                Second = second;
                Requests = 0;
                Errors = 0;
                ServerErrors = 0;
                RequestBytes = 0;
                ResponseBytes = 0;
                TimedRequests = 0;
                DurationSum = 0;
                DurationMax = 0;
            }
        }


        /// <summary>
        /// Record that a Request has started (call RequestCompleted when it finishes, passing the value returned here)
        /// </summary>
        /// <returns>True if the Request was counted as Active</returns>
        public bool RequestStarted()
        {
            if (!MetricsEnabled) return false;

            // Interlocked instead of _lock so starting a Request doesn't contend with completing one
            var active = Interlocked.Increment(ref _activeRequests);
            var peak = Volatile.Read(ref _peakActiveRequests);
            while (active > peak)
            {
                var current = Interlocked.CompareExchange(ref _peakActiveRequests, active, peak);
                if (current == peak) break;
                peak = current;
            }

            return true;
        }

        /// <summary>
        /// Record a completed Request
        /// </summary>
        /// <param name="duration">The Response Duration in milliseconds (null for Upgraded requests, which stay open for the life of the connection)</param>
        /// <param name="started">The value returned by RequestStarted</param>
        public void RequestCompleted(string area, string method, string route, string clientAddress, int statusCode, long requestBytes, long responseBytes, double? duration, DateTime requested, bool started)
        {
            if (!MetricsEnabled)
            {
                // Metrics may have been disabled (Runtime restart) while the Request was open
                if (started) DecrementActiveRequests();
                return;
            }

            var isError = statusCode >= 400;
            var isServerError = statusCode >= 500;
            var now = DateTime.UtcNow;

            if (started) DecrementActiveRequests();

            lock (_lock)
            {
                _totalRequests++;
                if (isError) _totalErrors++;
                if (isServerError) _totalServerErrors++;
                _totalRequestBytes += requestBytes;
                _totalResponseBytes += responseBytes;
                _lastRequested = requested;
                _statusCodes[statusCode] = _statusCodes.GetValueOrDefault(statusCode) + 1;

                var bucket = GetBucket(now);
                bucket.Requests++;
                if (isError) bucket.Errors++;
                if (isServerError) bucket.ServerErrors++;
                bucket.RequestBytes += requestBytes;
                bucket.ResponseBytes += responseBytes;
                if (duration.HasValue)
                {
                    bucket.TimedRequests++;
                    bucket.DurationSum += duration.Value;
                    bucket.DurationMax = Math.Max(bucket.DurationMax, duration.Value);
                }

                area ??= OtherName;
                GetGroup(_areas, area, OtherName, false, area, null, null)
                    .Add(statusCode, requestBytes, responseBytes, duration, requested);

                GetGroup(_endpoints, new EndpointKey(method, route, false), OtherEndpointKey, true, route, method, area)
                    .Add(statusCode, requestBytes, responseBytes, duration, requested);

                if (!string.IsNullOrEmpty(clientAddress))
                {
                    GetGroup(_clients, clientAddress, OtherName, true, clientAddress, null, null)
                        .Add(statusCode, requestBytes, responseBytes, duration, requested);
                }
            }
        }


        public TrakHoundHttpMetrics GetMetrics()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                var metrics = new TrakHoundHttpMetrics();
                metrics.Window = MetricsWindow;
                metrics.ActiveRequestCount = Volatile.Read(ref _activeRequests);
                metrics.PeakActiveRequestCount = Volatile.Read(ref _peakActiveRequests);
                metrics.TotalRequestCount = _totalRequests;
                metrics.TotalErrorCount = _totalErrors;
                metrics.TotalServerErrorCount = _totalServerErrors;
                metrics.TotalRequestBytes = _totalRequestBytes;
                metrics.TotalResponseBytes = _totalResponseBytes;
                metrics.LastRequested = _lastRequested;

                // Window values use complete seconds only (the current second is still filling)
                long requests = 0, errors = 0, serverErrors = 0, requestBytes = 0, responseBytes = 0, timedRequests = 0;
                double durationSum = 0, durationMax = 0;
                foreach (var bucket in _buckets)
                {
                    if (bucket.Second >= now - MetricsWindow && bucket.Second < now)
                    {
                        requests += bucket.Requests;
                        errors += bucket.Errors;
                        serverErrors += bucket.ServerErrors;
                        requestBytes += bucket.RequestBytes;
                        responseBytes += bucket.ResponseBytes;
                        timedRequests += bucket.TimedRequests;
                        durationSum += bucket.DurationSum;
                        durationMax = Math.Max(durationMax, bucket.DurationMax);
                    }
                }

                metrics.RequestRate = Math.Round((double)requests / MetricsWindow, 4);
                metrics.ErrorRate = Math.Round((double)errors / MetricsWindow, 4);
                metrics.ServerErrorRate = Math.Round((double)serverErrors / MetricsWindow, 4);
                metrics.RequestByteRate = Math.Round((double)requestBytes / MetricsWindow, 4);
                metrics.ResponseByteRate = Math.Round((double)responseBytes / MetricsWindow, 4);
                metrics.AverageDuration = timedRequests > 0 ? Math.Round(durationSum / timedRequests, 4) : 0;
                metrics.MaxDuration = Math.Round(durationMax, 4);

                metrics.Areas = _areas.Values.Select(o => o.ToMetrics()).OrderByDescending(o => o.TotalRequestCount).ToList();
                metrics.Endpoints = _endpoints.Values.Select(o => o.ToMetrics()).OrderByDescending(o => o.TotalRequestCount).ToList();
                metrics.Clients = _clients.Values.Select(o => o.ToMetrics()).OrderByDescending(o => o.TotalRequestCount).ToList();
                metrics.StatusCodes = _statusCodes
                    .Select(o => new TrakHoundHttpStatusCodeMetrics { StatusCode = o.Key, Count = o.Value })
                    .OrderBy(o => o.StatusCode)
                    .ToList();

                return metrics;
            }
        }


        // Floors at zero (as before) in case the counter was reset while the Request was open
        private void DecrementActiveRequests()
        {
            var active = Volatile.Read(ref _activeRequests);
            while (true)
            {
                var current = Interlocked.CompareExchange(ref _activeRequests, Math.Max(0, active - 1), active);
                if (current == active) return;
                active = current;
            }
        }

        // Call within _lock (the new group's values are passed instead of a factory so no closure is allocated per Request)
        private static GroupState GetGroup<TKey>(Dictionary<TKey, GroupState> groups, TKey key, TKey otherKey, bool limit, string name, string method, string area)
        {
            if (groups.TryGetValue(key, out var group)) return group;

            if (limit && groups.Count >= MaxGroups)
            {
                if (!groups.TryGetValue(otherKey, out group))
                {
                    group = new GroupState { Name = OtherName };
                    groups.Add(otherKey, group);
                }
                return group;
            }

            group = new GroupState { Name = name, Method = method, Area = area };
            groups.Add(key, group);
            return group;
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
