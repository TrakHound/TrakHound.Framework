// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;

namespace TrakHound.Services
{
    /// <summary>
    /// Collects Request Counts, Errors and Durations for each Service from the requests Routers send to them.
    /// Totals are kept since the Instance started; Rates, Average and Max are calculated over <see cref="MetricsWindow"/>.
    /// </summary>
    public class TrakHoundServiceMetricsCollector : ITrakHoundServiceMetricsRecorder
    {
        /// <summary>
        /// The window (in seconds) that Rates, Average and Max values are calculated over
        /// </summary>
        public const int MetricsWindow = 10;

        // Each Service's State has its own lock so calls to different Services don't contend (every Service call is recorded)
        private readonly ConcurrentDictionary<string, ServiceState> _services = new ConcurrentDictionary<string, ServiceState>(); // Service Configuration ID => State
        private static readonly ConcurrentDictionary<Type, string> _typeNames = new ConcurrentDictionary<Type, string>();


        // Totals for one second (a ring of these makes up the Metrics window)
        private class SecondBucket
        {
            public long Second;
            public long Requests;
            public long Errors;
            public double DurationSum;
            public double DurationMax;

            public void Reset(long second)
            {
                Second = second;
                Requests = 0;
                Errors = 0;
                DurationSum = 0;
                DurationMax = 0;
            }
        }

        private class Totals
        {
            public long Requests;
            public long Errors;
            public double DurationSum;
            public double DurationMax;
            public int LastStatusCode;
            public DateTime? LastRequested;

            public void Add(bool isError, double duration, int statusCode, DateTime requested)
            {
                Requests++;
                if (isError) Errors++;
                DurationSum += duration;
                DurationMax = Math.Max(DurationMax, duration);
                LastStatusCode = statusCode;
                LastRequested = requested;
            }

            public double Average => Requests > 0 ? Math.Round(DurationSum / Requests, 4) : 0;
        }

        private class OperationState : Totals
        {
            public string Type;
            public string Operation;
        }

        private class ServiceState : Totals
        {
            public string ServiceId;
            public string Name;
            public string PackageId;
            public string PackageVersion;
            public DateTime? LastError;
            public string LastErrorMessage;

            // One extra bucket for the current (incomplete) second
            public readonly SecondBucket[] Buckets = Enumerable.Range(0, MetricsWindow + 1).Select(o => new SecondBucket { Second = -1 }).ToArray();
            public readonly Dictionary<(string Type, string Operation), OperationState> Operations = new Dictionary<(string Type, string Operation), OperationState>(); // (Type, Operation) => State
            public readonly Dictionary<string, Totals> Routers = new Dictionary<string, Totals>(); // Router ID => Totals
        }


        public void Record(string routerId, ITrakHoundService service, Type serviceType, string operation, int statusCode, string message, TimeSpan duration)
        {
            var serviceId = service?.Configuration?.Id ?? service?.Id;
            if (string.IsNullOrEmpty(serviceId)) return;

            var now = DateTime.UtcNow;
            var second = now.ToUnixTimeSeconds();
            var isError = statusCode >= 400;
            var milliseconds = Math.Max(0, duration.TotalMilliseconds);
            var type = GetTypeName(serviceType);

            if (!_services.TryGetValue(serviceId, out var state))
            {
                state = _services.GetOrAdd(serviceId, static id => new ServiceState { ServiceId = id });
            }

            lock (state)
            {
                state.Name = service.Configuration?.Name ?? service.Name;
                state.PackageId = service.Configuration?.PackageId;
                state.PackageVersion = service.Configuration?.PackageVersion;
                state.Add(isError, milliseconds, statusCode, now);

                if (isError)
                {
                    state.LastError = now;
                    state.LastErrorMessage = !string.IsNullOrEmpty(message) ? $"{statusCode} : {message}" : statusCode.ToString();
                }

                var bucket = state.Buckets[second % state.Buckets.Length];
                if (bucket.Second != second) bucket.Reset(second);
                bucket.Requests++;
                if (isError) bucket.Errors++;
                bucket.DurationSum += milliseconds;
                bucket.DurationMax = Math.Max(bucket.DurationMax, milliseconds);

                var operationKey = (type, operation);
                if (!state.Operations.TryGetValue(operationKey, out var operationState))
                {
                    operationState = new OperationState { Type = type, Operation = operation };
                    state.Operations.Add(operationKey, operationState);
                }
                operationState.Add(isError, milliseconds, statusCode, now);

                var routerKey = routerId ?? string.Empty;
                if (!state.Routers.TryGetValue(routerKey, out var routerState))
                {
                    routerState = new Totals();
                    state.Routers.Add(routerKey, routerState);
                }
                routerState.Add(isError, milliseconds, statusCode, now);
            }
        }


        /// <summary>
        /// Gets the Metrics for each Service that has received requests
        /// </summary>
        public IEnumerable<TrakHoundServiceMetrics> GetMetrics()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            return _services.Values.Select(o =>
            {
                lock (o) return CreateMetrics(o, now);
            }).ToList();
        }

        /// <summary>
        /// Gets the Metrics for the specified Service (null if it has not received any requests)
        /// </summary>
        public TrakHoundServiceMetrics GetMetrics(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId)) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!_services.TryGetValue(serviceId, out var state)) return null;

            lock (state)
            {
                return CreateMetrics(state, now);
            }
        }


        private static TrakHoundServiceMetrics CreateMetrics(ServiceState state, long now)
        {
            var metrics = new TrakHoundServiceMetrics();
            metrics.ServiceId = state.ServiceId;
            metrics.Name = state.Name;
            metrics.PackageId = state.PackageId;
            metrics.PackageVersion = state.PackageVersion;
            metrics.Window = MetricsWindow;
            metrics.TotalRequestCount = state.Requests;
            metrics.TotalErrorCount = state.Errors;
            metrics.LastRequested = state.LastRequested;
            metrics.LastError = state.LastError;
            metrics.LastErrorMessage = state.LastErrorMessage;

            // Window values use complete seconds only (the current second is still filling)
            long requests = 0, errors = 0;
            double durationSum = 0, durationMax = 0;
            foreach (var bucket in state.Buckets)
            {
                if (bucket.Second >= now - MetricsWindow && bucket.Second < now)
                {
                    requests += bucket.Requests;
                    errors += bucket.Errors;
                    durationSum += bucket.DurationSum;
                    durationMax = Math.Max(durationMax, bucket.DurationMax);
                }
            }

            metrics.RequestRate = Math.Round((double)requests / MetricsWindow, 4);
            metrics.ErrorRate = Math.Round((double)errors / MetricsWindow, 4);
            metrics.AverageDuration = requests > 0 ? Math.Round(durationSum / requests, 4) : 0;
            metrics.MaxDuration = Math.Round(durationMax, 4);

            metrics.Operations = state.Operations.Values
                .Select(o => new TrakHoundServiceOperationMetrics
                {
                    Type = o.Type,
                    Operation = o.Operation,
                    TotalRequestCount = o.Requests,
                    TotalErrorCount = o.Errors,
                    AverageDuration = o.Average,
                    MaxDuration = Math.Round(o.DurationMax, 4),
                    LastStatusCode = o.LastStatusCode,
                    LastRequested = o.LastRequested
                })
                .OrderByDescending(o => o.TotalRequestCount)
                .ToList();

            metrics.Routers = state.Routers
                .Select(o => new TrakHoundServiceRouterMetrics
                {
                    RouterId = o.Key,
                    TotalRequestCount = o.Value.Requests,
                    TotalErrorCount = o.Value.Errors,
                    AverageDuration = o.Value.Average,
                    LastRequested = o.Value.LastRequested
                })
                .OrderByDescending(o => o.TotalRequestCount)
                .ToList();

            return metrics;
        }

        /// <summary>
        /// ITrakHoundTableService => Table
        /// </summary>
        private static string GetTypeName(Type serviceType)
        {
            if (serviceType == null) return null;

            // Cached since this runs on every Service call
            if (_typeNames.TryGetValue(serviceType, out var typeName)) return typeName;
            return _typeNames.GetOrAdd(serviceType, CreateTypeName);
        }

        private static string CreateTypeName(Type serviceType)
        {
            var name = serviceType?.Name;
            if (string.IsNullOrEmpty(name)) return null;

            if (name.StartsWith("ITrakHound")) name = name.Substring("ITrakHound".Length);
            if (name.EndsWith("Service") && name.Length > "Service".Length) name = name.Substring(0, name.Length - "Service".Length);
            return name;
        }
    }
}
