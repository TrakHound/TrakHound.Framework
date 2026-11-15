// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Whether each Service can reach its external system (ex. a database or message broker), using the Service's CheckAvailability.
    /// Services are probed in the background (at most every <see cref="ProbeInterval"/>) so a slow or unreachable system doesn't delay the evaluation.
    /// </summary>
    public class ServicesHealthCheck : ITrakHoundHealthCheck
    {
        public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(15);

        // The default (not overridden) CheckAvailability returns this message
        private const string _notImplementedMessage = "Availability Is Not Implemented";

        private readonly ConcurrentDictionary<string, Probe> _probes = new ConcurrentDictionary<string, Probe>(); // Service Configuration ID => latest Probe


        private class Probe
        {
            public DateTime Started;
            public int Running; // 1 while a probe is in progress
            public volatile ProbeResult Result; // Replaced (not modified) by the background probe
        }

        private class ProbeResult
        {
            public DateTime Completed { get; init; }
            public bool IsAvailable { get; init; }
            public bool NotImplemented { get; init; }
            public string Message { get; init; }
        }


        public string Id => "services";

        public string Title => "Services";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var services = context.Instance.ServicesProvider?.Drivers?.Services?
                .Where(o => o != null && !string.IsNullOrEmpty(GetId(o)))
                .GroupBy(GetId)
                .Select(o => o.First())
                .ToList();

            if (services.IsNullOrEmpty())
            {
                _probes.Clear();
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, "No Services are configured", "diagnostics/services"));
            }

            // Forget removed Services
            foreach (var removed in _probes.Keys.Except(services.Select(GetId)).ToList()) _probes.TryRemove(removed, out _);

            var items = new List<TrakHoundHealthCheckItem>();
            var pending = 0;
            var notImplemented = 0;

            foreach (var service in services)
            {
                var id = GetId(service);
                var probe = _probes.GetOrAdd(id, _ => new Probe());

                // Start a new probe in the background when due (results are used on a later evaluation)
                if ((probe.Result == null || context.Now - probe.Started >= ProbeInterval) && Interlocked.CompareExchange(ref probe.Running, 1, 0) == 0)
                {
                    probe.Started = context.Now;
                    _ = Task.Run(() => RunProbe(service, probe)); // A Service may block before its first await
                }

                var result = probe.Result;
                if (result == null)
                {
                    pending++;
                    continue;
                }

                if (result.NotImplemented) notImplemented++;

                if (!result.IsAvailable)
                {
                    items.Add(new TrakHoundHealthCheckItem
                    {
                        Id = id,
                        Name = GetName(service),
                        Status = TrakHoundHealthStatus.Degraded,
                        Description = $"Not Available{(!string.IsNullOrEmpty(result.Message) ? $" : {result.Message}" : "")} (checked {result.Completed.ToLocalTime():HH:mm:ss})",
                        Link = $"diagnostics/services?ServiceId={id}"
                    });
                }
            }

            if (items.Count > 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, $"{items.Count} of {services.Count} Services not available", "diagnostics/services", items));
            }

            if (pending == services.Count)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Unknown, "Checking Service availability..", "diagnostics/services"));
            }

            var summary = $"{services.Count - pending} of {services.Count} Services available";
            if (notImplemented > 0) summary += $" · {notImplemented} don't report availability";
            if (pending > 0) summary += $" · {pending} being checked";
            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, summary, "diagnostics/services"));
        }


        private static async Task RunProbe(ITrakHoundService service, Probe probe)
        {
            bool isAvailable = false, notImplemented = false;
            string message;

            try
            {
                using var cancellation = new CancellationTokenSource(_probeTimeout);
                var check = service.CheckAvailability(cancellation.Token);

                if (await Task.WhenAny(check, Task.Delay(_probeTimeout)) == check)
                {
                    var response = await check;
                    var availability = response.Result;

                    isAvailable = response.Success && availability != null && availability.IsAvailable;
                    notImplemented = availability?.Message?.StartsWith(_notImplementedMessage, StringComparison.OrdinalIgnoreCase) == true;
                    message = availability?.Message ?? response.Message;
                }
                else
                {
                    message = $"No response within {_probeTimeout.TotalSeconds:0} seconds";
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            probe.Result = new ProbeResult { Completed = DateTime.UtcNow, IsAvailable = isAvailable, NotImplemented = notImplemented, Message = message };
            Interlocked.Exchange(ref probe.Running, 0);
        }

        private static string GetId(ITrakHoundService service) => service.Configuration?.Id ?? service.Id;

        private static string GetName(ITrakHoundService service) => !string.IsNullOrEmpty(service.Configuration?.Name) ? service.Configuration.Name : GetId(service);
    }
}
