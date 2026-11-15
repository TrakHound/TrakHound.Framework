// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.RateLimiting;
using TrakHound.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// What a Rate Limit applies to (see TrakHoundInstanceRateLimitConfiguration)
    /// </summary>
    public static class TrakHoundRateLimitAreas
    {
        public const string Api = "api";
        public const string SystemApi = "systemApi";
        public const string McpTools = "mcpTools";
    }


    /// <summary>
    /// Limits the Requests each caller makes to an area (see TrakHoundInstanceRateLimitConfiguration). The Configuration is read on every
    /// Request, so saved changes apply immediately (the limits, and what each caller has used, are reset when an area's limits change)
    /// </summary>
    public class TrakHoundRateLimiter : IDisposable
    {
        private readonly Func<TrakHoundInstanceRateLimitConfiguration> _getConfiguration;
        private readonly Dictionary<string, AreaLimiter> _areas = new Dictionary<string, AreaLimiter>();
        private readonly object _lock = new object();


        public TrakHoundRateLimiter(Func<TrakHoundInstanceRateLimitConfiguration> getConfiguration)
        {
            _getConfiguration = getConfiguration;
        }


        /// <summary>
        /// Acquires a Request for the caller. Dispose the Lease when the Request completes (it holds the caller's place in the Concurrency Limit).
        /// Streams (WebSockets and Server-Sent Events) should not count toward the Concurrency Limit, since they stay open
        /// </summary>
        public TrakHoundRateLimitLease Acquire(string area, string callerKey, bool countConcurrency = true)
        {
            var configuration = _getConfiguration?.Invoke();
            if (configuration == null || !configuration.Enabled) return TrakHoundRateLimitLease.Allowed;

            var limit = area switch
            {
                TrakHoundRateLimitAreas.Api => configuration.Api,
                TrakHoundRateLimitAreas.SystemApi => configuration.SystemApi,
                TrakHoundRateLimitAreas.McpTools => configuration.McpTools,
                _ => null
            };
            if (limit == null || (limit.PermitLimit <= 0 && limit.ConcurrencyLimit <= 0)) return TrakHoundRateLimitLease.Allowed;

            return GetAreaLimiter(area, limit).Acquire(callerKey ?? "", countConcurrency);
        }

        /// <summary>
        /// The key that a caller's Requests are counted under : the Identity, or the IP Address for an anonymous Request
        /// </summary>
        public static string GetCallerKey(TrakHoundIdentity identity, HttpContext httpContext)
        {
            if (identity != null) return $"identity:{identity.ProviderId}:{identity.UserId}";

            var address = httpContext?.Connection.RemoteIpAddress;
            if (address != null && address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return $"address:{address}";
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var area in _areas.Values) area.Dispose();
                _areas.Clear();
            }
        }


        private AreaLimiter GetAreaLimiter(string area, TrakHoundRateLimit limit)
        {
            lock (_lock)
            {
                if (_areas.TryGetValue(area, out var existing))
                {
                    if (existing.Matches(limit)) return existing;
                    existing.Dispose();
                }

                var areaLimiter = new AreaLimiter(limit);
                _areas[area] = areaLimiter;
                return areaLimiter;
            }
        }


        /// <summary>
        /// The Rate and Concurrency Limiters for an area (one partition for each caller)
        /// </summary>
        private sealed class AreaLimiter : IDisposable
        {
            // Retry-After for a Request rejected by the Concurrency Limit (when one of the caller's Requests completes is unknown)
            private static readonly TimeSpan _concurrencyRetryAfter = TimeSpan.FromSeconds(1);

            private readonly int _permitLimit;
            private readonly int _window;
            private readonly int _concurrencyLimit;
            private readonly PartitionedRateLimiter<string> _rateLimiter;
            private readonly PartitionedRateLimiter<string> _concurrencyLimiter;


            public AreaLimiter(TrakHoundRateLimit limit)
            {
                _permitLimit = Math.Max(0, limit.PermitLimit);
                _window = Math.Clamp(limit.Window, 1, TrakHoundRateLimit.MaxWindow);
                _concurrencyLimit = Math.Max(0, limit.ConcurrencyLimit);

                if (_permitLimit > 0)
                {
                    // Replenish evenly over the Window (at most 4 times a second), so a caller that uses its burst gets Requests back gradually
                    var periods = Math.Min(_permitLimit, _window * 4);
                    var tokenBucket = new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = _permitLimit,
                        TokensPerPeriod = (int)Math.Ceiling((double)_permitLimit / periods),
                        ReplenishmentPeriod = TimeSpan.FromSeconds((double)_window / periods),
                        QueueLimit = 0,
                        // The PartitionedRateLimiter's 100ms heartbeat replenishes the partitions (AutoReplenishment would add a Timer for every caller)
                        AutoReplenishment = false
                    };

                    _rateLimiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetTokenBucketLimiter(key, _ => tokenBucket));
                }

                if (_concurrencyLimit > 0)
                {
                    var concurrency = new ConcurrencyLimiterOptions { PermitLimit = _concurrencyLimit, QueueLimit = 0 };
                    _concurrencyLimiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetConcurrencyLimiter(key, _ => concurrency));
                }
            }


            public bool Matches(TrakHoundRateLimit limit)
            {
                return Math.Max(0, limit.PermitLimit) == _permitLimit &&
                    Math.Clamp(limit.Window, 1, TrakHoundRateLimit.MaxWindow) == _window &&
                    Math.Max(0, limit.ConcurrencyLimit) == _concurrencyLimit;
            }

            public TrakHoundRateLimitLease Acquire(string callerKey, bool countConcurrency)
            {
                RateLimitLease concurrencyLease = null;
                if (_concurrencyLimiter != null && countConcurrency)
                {
                    concurrencyLease = _concurrencyLimiter.AttemptAcquire(callerKey);
                    if (!concurrencyLease.IsAcquired)
                    {
                        concurrencyLease.Dispose();
                        return TrakHoundRateLimitLease.Rejected($"No more than {_concurrencyLimit} Requests at the same time", _concurrencyRetryAfter);
                    }
                }

                if (_rateLimiter != null)
                {
                    using var rateLease = _rateLimiter.AttemptAcquire(callerKey);
                    if (!rateLease.IsAcquired)
                    {
                        concurrencyLease?.Dispose();

                        var retryAfter = rateLease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : TimeSpan.FromSeconds((double)_window / _permitLimit);
                        return TrakHoundRateLimitLease.Rejected($"No more than {_permitLimit} Requests every {_window} seconds", retryAfter);
                    }
                }

                return concurrencyLease != null ? new TrakHoundRateLimitLease(concurrencyLease) : TrakHoundRateLimitLease.Allowed;
            }

            public void Dispose()
            {
                _rateLimiter?.Dispose();
                _concurrencyLimiter?.Dispose();
            }
        }
    }


    /// <summary>
    /// Whether a Request was allowed by the Rate Limits. Dispose when the Request completes
    /// </summary>
    public sealed class TrakHoundRateLimitLease : IDisposable
    {
        public static readonly TrakHoundRateLimitLease Allowed = new TrakHoundRateLimitLease(null);

        private readonly RateLimitLease _lease;


        public bool IsAcquired { get; private init; } = true;

        /// <summary>
        /// Which limit the Request was rejected by (null when allowed)
        /// </summary>
        public string Reason { get; private init; }

        /// <summary>
        /// When the caller can try again (null when allowed)
        /// </summary>
        public TimeSpan? RetryAfter { get; private init; }


        internal TrakHoundRateLimitLease(RateLimitLease lease)
        {
            _lease = lease;
        }

        internal static TrakHoundRateLimitLease Rejected(string reason, TimeSpan retryAfter)
        {
            return new TrakHoundRateLimitLease(null) { IsAcquired = false, Reason = reason, RetryAfter = retryAfter };
        }

        /// <summary>
        /// The Retry-After value in whole Seconds (at least 1)
        /// </summary>
        public int GetRetryAfterSeconds() => Math.Max(1, (int)Math.Ceiling((RetryAfter ?? TimeSpan.Zero).TotalSeconds));

        public void Dispose() => _lease?.Dispose();
    }
}
