using System.Collections.Concurrent;
using TrakHound.Api;

namespace TrakHound.Instance.Api
{
    public class TrakHoundApiConnectionManager
    {
        /// <summary>
        /// The window (in seconds) that Rates, Average and Max values are calculated over
        /// </summary>
        public const int MetricsWindow = 10;

        /// <summary>
        /// Sources include HTTP remote addresses (unbounded on a public Instance), so each API keeps the most recently seen Sources up to this limit
        /// </summary>
        private const int MaxSources = 1000;

        // The least recently seen Sources removed at once when MaxSources is reached (so the sort isn't done for every new Source)
        private const int SourceEvictionCount = 100;

        public const int DefaultRequestHistory = TrakHoundInstanceMetricsConfiguration.DefaultHistory;
        public const int MaxRequestHistory = 10000;

        private CircularBuffer<TrakHoundApiRequestRecord> _buffer = new(DefaultRequestHistory);
        private long _lastRequestId;
        private readonly Dictionary<string, ApiState> _apis = new(); // ApiId => State
        private readonly object _lock = new object();
        private readonly ConcurrentQueue<TrakHoundApiRequestRecord> _pending = new(); // Requests not yet applied to the Metrics


        /// <summary>
        /// Calculate Metrics from recorded requests (the recent request Buffer is always kept)
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;

        /// <summary>
        /// The most recent Requests kept (0 keeps none). Changing it clears the Requests kept so far.
        /// </summary>
        public int RequestHistory
        {
            get => _buffer?.Limit ?? 0;
            set
            {
                var capacity = Math.Clamp(value, 0, MaxRequestHistory);
                if (capacity == RequestHistory) return;

                _buffer = capacity > 0 ? new CircularBuffer<TrakHoundApiRequestRecord>(capacity) : null;
            }
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
                DurationSum = 0;
                DurationMax = 0;
            }
        }

        class EndpointState
        {
            public string Type;
            public string Method;
            public string Route;
            public long Requests;
            public long Errors;
            public double DurationSum;
            public double DurationMax;
            public int LastStatusCode;
            public DateTime LastRequested;
        }

        class SourceState
        {
            public TrakHoundApiRequestSourceType Type;
            public string Id;
            public string Name;
            public long Requests;
            public long Errors;
            public double DurationSum;
            public double DurationMax;
            public DateTime LastRequested;
        }

        class ApiState
        {
            public string ApiId;
            public string PackageId;
            public string PackageVersion;
            public long Requests;
            public long Errors;
            public long ServerErrors;
            public long RequestBytes;
            public long ResponseBytes;
            public DateTime? LastRequested;

            // One extra bucket for the current (incomplete) second
            public readonly SecondBucket[] Buckets = Enumerable.Range(0, MetricsWindow + 1).Select(o => new SecondBucket { Second = -1 }).ToArray();
            public readonly Dictionary<string, EndpointState> Endpoints = new();
            public readonly Dictionary<string, SourceState> Sources = new();
        }


        /// <summary>
        /// Records a completed Request (in the Recent Requests, and in the Metrics when they are enabled). Assigns the Request's Id.
        /// </summary>
        public void Update(TrakHoundApiRequestRecord request)
        {
            if (request != null && !string.IsNullOrEmpty(request.ApiId))
            {
                var buffer = _buffer;
                if (buffer != null)
                {
                    request.Id = Interlocked.Increment(ref _lastRequestId);
                    buffer.Add(request);
                }

                if (!MetricsEnabled) return;

                // Requests are queued and applied by whichever Request holds the lock, so concurrent Requests don't wait on each other.
                // A Request that finds the lock taken leaves its record for the holder, the next Request or the next GetMetrics
                _pending.Enqueue(request);
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        ApplyPending();
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
            }
        }

        // Called while holding _lock
        private void ApplyPending()
        {
            while (_pending.TryDequeue(out var request)) Apply(request);
        }

        private void Apply(TrakHoundApiRequestRecord request)
        {
            var isError = request.StatusCode >= 400;
            var isServerError = request.StatusCode >= 500;
            var duration = Math.Max(0, request.Duration.TotalMilliseconds);
            var second = request.Completed.ToUnixTimeSeconds();

            if (!_apis.TryGetValue(request.ApiId, out var api))
            {
                api = new ApiState { ApiId = request.ApiId };
                _apis.Add(request.ApiId, api);
            }

            api.PackageId = request.PackageId;
            api.PackageVersion = request.PackageVersion;
            api.Requests++;
            if (isError) api.Errors++;
            if (isServerError) api.ServerErrors++;
            api.RequestBytes += request.RequestBytes;
            api.ResponseBytes += request.ResponseBytes;
            api.LastRequested = request.Requested;

            var bucket = api.Buckets[second % api.Buckets.Length];
            if (bucket.Second != second) bucket.Reset(second);
            bucket.Requests++;
            if (isError) bucket.Errors++;
            if (isServerError) bucket.ServerErrors++;
            bucket.RequestBytes += request.RequestBytes;
            bucket.ResponseBytes += request.ResponseBytes;
            bucket.DurationSum += duration;
            bucket.DurationMax = Math.Max(bucket.DurationMax, duration);

            var endpointKey = $"{request.EndpointType}:{request.Method}:{request.Route}";
            if (!api.Endpoints.TryGetValue(endpointKey, out var endpoint))
            {
                endpoint = new EndpointState { Type = request.EndpointType.ToString(), Method = request.Method.ToString(), Route = request.Route };
                api.Endpoints.Add(endpointKey, endpoint);
            }

            endpoint.Requests++;
            if (isError) endpoint.Errors++;
            endpoint.DurationSum += duration;
            endpoint.DurationMax = Math.Max(endpoint.DurationMax, duration);
            endpoint.LastStatusCode = request.StatusCode;
            endpoint.LastRequested = request.Requested;

            var sourceKey = $"{request.SourceType}:{request.SourceId}";
            if (!api.Sources.TryGetValue(sourceKey, out var sourceState))
            {
                if (api.Sources.Count >= MaxSources)
                {
                    var evictKeys = api.Sources.OrderBy(o => o.Value.LastRequested).Take(SourceEvictionCount).Select(o => o.Key).ToList();
                    foreach (var evictKey in evictKeys) api.Sources.Remove(evictKey);
                }

                sourceState = new SourceState { Type = request.SourceType, Id = request.SourceId };
                api.Sources.Add(sourceKey, sourceState);
            }

            sourceState.Name = request.SourceName;
            sourceState.Requests++;
            if (isError) sourceState.Errors++;
            sourceState.DurationSum += duration;
            sourceState.DurationMax = Math.Max(sourceState.DurationMax, duration);
            sourceState.LastRequested = request.Requested;
        }

        /// <summary>
        /// The Requests kept, newest first
        /// </summary>
        public TrakHoundApiRecentRequests GetRecentRequests()
        {
            var buffer = _buffer;
            return new TrakHoundApiRecentRequests
            {
                Capacity = buffer?.Limit ?? 0,
                Requests = buffer?.Get() ?? Array.Empty<TrakHoundApiRequestRecord>()
            };
        }


        /// <summary>
        /// Gets the Metrics for all APIs that have received Requests
        /// </summary>
        public IEnumerable<TrakHoundApiMetrics> GetMetrics()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                ApplyPending();
                return _apis.Values.Select(o => CreateMetrics(o, now)).ToList();
            }
        }

        /// <summary>
        /// Gets the Metrics for the specified API (null if it has not received any Requests)
        /// </summary>
        public TrakHoundApiMetrics GetMetrics(string apiId)
        {
            if (string.IsNullOrEmpty(apiId)) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                ApplyPending();
                return _apis.TryGetValue(apiId, out var api) ? CreateMetrics(api, now) : null;
            }
        }

        private static TrakHoundApiMetrics CreateMetrics(ApiState api, long now)
        {
            var metrics = new TrakHoundApiMetrics();
            metrics.ApiId = api.ApiId;
            metrics.PackageId = api.PackageId;
            metrics.PackageVersion = api.PackageVersion;
            metrics.Window = MetricsWindow;
            metrics.TotalRequestCount = api.Requests;
            metrics.TotalErrorCount = api.Errors;
            metrics.TotalServerErrorCount = api.ServerErrors;
            metrics.TotalRequestBytes = api.RequestBytes;
            metrics.TotalResponseBytes = api.ResponseBytes;
            metrics.LastRequested = api.LastRequested;

            var cpuMonitor = TrakHoundModuleCpuMonitors.Default.FindMonitor(TrakHound.Instances.TrakHoundCpuModuleTypes.Api, api.ApiId);
            metrics.CpuUsage = Math.Round(cpuMonitor?.Usage ?? 0, 2);
            metrics.CpuTime = Math.Round(cpuMonitor?.TotalProcessorTime.TotalMilliseconds ?? 0);

            // Window values use complete seconds only (the current second is still filling)
            long requests = 0, errors = 0, serverErrors = 0, requestBytes = 0, responseBytes = 0;
            double durationSum = 0, durationMax = 0;
            foreach (var bucket in api.Buckets)
            {
                if (bucket.Second >= now - MetricsWindow && bucket.Second < now)
                {
                    requests += bucket.Requests;
                    errors += bucket.Errors;
                    serverErrors += bucket.ServerErrors;
                    requestBytes += bucket.RequestBytes;
                    responseBytes += bucket.ResponseBytes;
                    durationSum += bucket.DurationSum;
                    durationMax = Math.Max(durationMax, bucket.DurationMax);
                }
            }

            metrics.RequestRate = Math.Round((double)requests / MetricsWindow, 4);
            metrics.ErrorRate = Math.Round((double)errors / MetricsWindow, 4);
            metrics.ServerErrorRate = Math.Round((double)serverErrors / MetricsWindow, 4);
            metrics.RequestByteRate = Math.Round((double)requestBytes / MetricsWindow, 4);
            metrics.ResponseByteRate = Math.Round((double)responseBytes / MetricsWindow, 4);
            metrics.AverageDuration = requests > 0 ? Math.Round(durationSum / requests, 4) : 0;
            metrics.MaxDuration = Math.Round(durationMax, 4);

            metrics.Endpoints = api.Endpoints.Values
                .Select(o =>
                {
                    var endpointCpuMonitor = cpuMonitor != null ? TrakHoundModuleCpuMonitors.Default.FindMonitor(TrakHound.Instances.TrakHoundCpuModuleTypes.Api, api.ApiId, GetCpuKey(o.Type, o.Method, o.Route)) : null;
                    return new TrakHoundApiEndpointMetrics
                    {
                        Type = o.Type,
                        Method = o.Method,
                        Route = o.Route,
                        TotalRequestCount = o.Requests,
                        TotalErrorCount = o.Errors,
                        AverageDuration = o.Requests > 0 ? Math.Round(o.DurationSum / o.Requests, 4) : 0,
                        MaxDuration = Math.Round(o.DurationMax, 4),
                        LastStatusCode = o.LastStatusCode,
                        LastRequested = o.LastRequested,
                        CpuUsage = Math.Round(endpointCpuMonitor?.Usage ?? 0, 2),
                        CpuTime = Math.Round(endpointCpuMonitor?.TotalProcessorTime.TotalMilliseconds ?? 0)
                    };
                })
                .OrderByDescending(o => o.TotalRequestCount)
                .ToList();

            metrics.Sources = api.Sources.Values
                .Select(o => new TrakHoundApiSourceMetrics
                {
                    Type = o.Type.ToString(),
                    Id = o.Id,
                    Name = o.Name,
                    TotalRequestCount = o.Requests,
                    TotalErrorCount = o.Errors,
                    AverageDuration = o.Requests > 0 ? Math.Round(o.DurationSum / o.Requests, 4) : 0,
                    MaxDuration = Math.Round(o.DurationMax, 4),
                    LastRequested = o.LastRequested
                })
                .OrderByDescending(o => o.TotalRequestCount)
                .ToList();

            return metrics;
        }

        /// <summary>
        /// The key of an Endpoint's CPU Monitor (within its API's Monitor in TrakHoundModuleCpuMonitors), the same as the Endpoint's Metrics key
        /// </summary>
        public static string GetCpuKey(string endpointType, string method, string route) => $"{endpointType}:{method}:{route}";
    }
}
