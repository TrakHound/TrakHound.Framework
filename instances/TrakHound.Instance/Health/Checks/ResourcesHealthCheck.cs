// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Memory and CPU usage of the Instance process, and how long queued work waits for a Thread Pool thread
    /// (a long wait means the Thread Pool is starved, ex. by blocking calls, and everything in the Instance slows down)
    /// </summary>
    public class ResourcesHealthCheck : ITrakHoundHealthCheck
    {
        private const double _memoryDegraded = 80; // Percent of available memory
        private const double _memoryUnhealthy = 90;
        private const double _cpuDegraded = 95; // Percent of total machine capacity (average)
        private static readonly TimeSpan _cpuWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _threadPoolDegraded = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan _threadPoolUnhealthy = TimeSpan.FromSeconds(5);


        public string Id => "resources";

        public string Title => "Resources";


        public async Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var items = new List<TrakHoundHealthCheckItem>();
            var monitor = TrakHoundProcessMonitor.Default;

            // Memory
            var workingSet = monitor.Current?.WorkingSet ?? 0;
            var available = monitor.TotalAvailableMemory;
            if (workingSet > 0 && available > 0)
            {
                var percent = (double)workingSet / available * 100;
                var status = percent >= _memoryUnhealthy ? TrakHoundHealthStatus.Unhealthy : percent >= _memoryDegraded ? TrakHoundHealthStatus.Degraded : TrakHoundHealthStatus.Healthy;
                items.Add(Item("memory", "Memory", status, $"{FormatBytes(workingSet)} of {FormatBytes(available)} available ({percent:0.#}%)"));
            }

            // CPU (average, so short bursts are ignored)
            var samples = monitor.GetHistory(context.Now - _cpuWindow);
            if (!samples.IsNullOrEmpty())
            {
                var average = samples.Average(o => o.CpuUsage);
                var status = average >= _cpuDegraded ? TrakHoundHealthStatus.Degraded : TrakHoundHealthStatus.Healthy;
                items.Add(Item("cpu", "CPU", status, $"{average:0.#}% average over the last {_cpuWindow.TotalMinutes:0} minute ({monitor.ProcessorCount} processors)"));
            }

            // Thread Pool : time for queued work to start
            var latency = await MeasureThreadPoolLatency(_threadPoolUnhealthy + TimeSpan.FromSeconds(1));
            var threadPoolStatus = latency >= _threadPoolUnhealthy ? TrakHoundHealthStatus.Unhealthy : latency >= _threadPoolDegraded ? TrakHoundHealthStatus.Degraded : TrakHoundHealthStatus.Healthy;
            var latencyText = latency >= _threadPoolUnhealthy ? $"more than {_threadPoolUnhealthy.TotalSeconds:0} s" : $"{latency.TotalMilliseconds:0.#} ms";
            items.Add(Item("threadpool", "Thread Pool", threadPoolStatus, $"Queued work started after {latencyText} · {ThreadPool.ThreadCount} threads · {ThreadPool.PendingWorkItemCount} pending"));

            var overall = items.Max(o => o.Status);
            var description = overall == TrakHoundHealthStatus.Healthy
                ? string.Join(" · ", items.Select(o => $"{o.Name} {Summary(o)}"))
                : string.Join(" ; ", items.Where(o => o.Status == overall).Select(o => $"{o.Name} : {o.Description}"));

            // The Instance page shows the process CPU and Memory
            return context.Result(this, overall, description, "", items);
        }


        private static async Task<TimeSpan> MeasureThreadPoolLatency(TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            var probe = Task.Run(() => stopwatch.Elapsed);

            if (await Task.WhenAny(probe, Task.Delay(timeout)) == probe) return await probe;
            return timeout;
        }

        private static TrakHoundHealthCheckItem Item(string id, string name, TrakHoundHealthStatus status, string description)
        {
            return new TrakHoundHealthCheckItem { Id = id, Name = name, Status = status, Description = description };
        }

        // A short form of the Item for the Healthy summary
        private static string Summary(TrakHoundHealthCheckItem item)
        {
            var description = item.Description ?? "";
            var index = description.IndexOf(" (", StringComparison.Ordinal);
            if (item.Id == "memory" && index > 0) return description.Substring(index + 2).TrimEnd(')');
            if (item.Id == "cpu") return description.Split(' ')[0];
            if (item.Id == "threadpool") return "OK";
            return description;
        }

        private static string FormatBytes(double value)
        {
            if (value >= 1024 * 1024 * 1024) return $"{value / (1024 * 1024 * 1024):0.#} GB";
            if (value >= 1024 * 1024) return $"{value / (1024 * 1024):0} MB";
            return $"{value / 1024:0} KB";
        }
    }
}
