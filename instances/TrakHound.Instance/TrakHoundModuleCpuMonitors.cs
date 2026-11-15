// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Instances;

namespace TrakHound
{
    /// <summary>
    /// The CPU Monitors for each Module (Worker, Function, API, App) and Module Type, sampled every second with the process total
    /// so the CPU that isn't measured for any Module (Unattributed) can be shown too.
    /// </summary>
    /// <remarks>
    /// Monitors form a tree (Type > Module > Endpoint or Run), and CPU time recorded by one is added to its parents.
    /// A Module's time includes the code it calls in its ExecutionContext (ex. a Service called by an API is counted to the API), except other
    /// Modules: an API called by a Worker runs in the API's Monitor, so that time is counted to the API (the innermost Module).
    /// </remarks>
    public class TrakHoundModuleCpuMonitors : IDisposable
    {
        private static readonly Lazy<TrakHoundModuleCpuMonitors> _default = new(() => new TrakHoundModuleCpuMonitors());

        public static TrakHoundModuleCpuMonitors Default => _default.Value;

        public static readonly TimeSpan HistoryDuration = TimeSpan.FromMinutes(10);
        private const int _sampleInterval = 1000;

        private readonly Dictionary<string, TrakHoundCpuMonitor> _types = new(); // Type => Monitor
        private readonly Dictionary<string, Module> _modules = new(); // Type:Id => Module
        private readonly Queue<TrakHoundCpuUsageSample> _history = new();
        private readonly object _lock = new object();
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly Timer _timer;
        private TimeSpan _lastProcessorTime;
        private long _lastTimestamp;
        private double _processUsage;
        private int _sampling;


        class Module
        {
            public string Type;
            public string Id;
            public string Name;
            public TrakHoundCpuMonitor Monitor;
            public readonly Dictionary<string, TrakHoundCpuMonitor> Children = new();
        }


        /// <summary>
        /// Measure CPU usage by Module (Settings > Metrics > Measure CPU Usage). When false, no Monitors are returned (and none of the Module code is measured).
        /// </summary>
        public bool Enabled { get; set; } = true;


        private TrakHoundModuleCpuMonitors()
        {
            foreach (var type in TrakHoundCpuModuleTypes.All) _types.Add(type, new TrakHoundCpuMonitor());

            try { _lastProcessorTime = _process.TotalProcessorTime; } catch { }
            _lastTimestamp = Stopwatch.GetTimestamp();

            _timer = new Timer(Sample, null, _sampleInterval, _sampleInterval);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _process.Dispose();
        }


        /// <summary>
        /// The Monitor for a Module (created the first time it is requested). Null when CPU monitoring is disabled.
        /// </summary>
        public TrakHoundCpuMonitor GetMonitor(string type, string id, string name = null)
        {
            if (!Enabled || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id) || !_types.TryGetValue(type, out var typeMonitor)) return null;

            lock (_lock)
            {
                var key = $"{type}:{id}";
                if (!_modules.TryGetValue(key, out var module))
                {
                    module = new Module { Type = type, Id = id, Monitor = new TrakHoundCpuMonitor(typeMonitor) };
                    _modules.Add(key, module);
                }

                if (!string.IsNullOrEmpty(name)) module.Name = name;
                return module.Monitor;
            }
        }

        /// <summary>
        /// The Monitor for part of a Module (ex. an API Endpoint), whose time is added to the Module's. Null when CPU monitoring is disabled.
        /// </summary>
        public TrakHoundCpuMonitor GetChildMonitor(string type, string id, string childKey)
        {
            var moduleMonitor = GetMonitor(type, id);
            if (moduleMonitor == null || string.IsNullOrEmpty(childKey)) return moduleMonitor;

            lock (_lock)
            {
                var module = _modules[$"{type}:{id}"];
                if (!module.Children.TryGetValue(childKey, out var monitor))
                {
                    monitor = new TrakHoundCpuMonitor(moduleMonitor);
                    module.Children.Add(childKey, monitor);
                }

                return monitor;
            }
        }

        /// <summary>
        /// The Monitor for a Module, or part of it, if it has been measured (null otherwise). Used to read the Usage without creating a Monitor.
        /// </summary>
        public TrakHoundCpuMonitor FindMonitor(string type, string id, string childKey = null)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id)) return null;

            lock (_lock)
            {
                if (!_modules.TryGetValue($"{type}:{id}", out var module)) return null;
                if (string.IsNullOrEmpty(childKey)) return module.Monitor;
                return module.Children.GetValueOrDefault(childKey);
            }
        }

        public TrakHoundCpuUsage GetUsage(bool includeHistory = true)
        {
            var usage = new TrakHoundCpuUsage();
            usage.Enabled = Enabled;
            usage.ThreadTimeSupported = TrakHoundCpuMonitor.ThreadTimeSupported;
            usage.ProcessorCount = Environment.ProcessorCount;

            lock (_lock)
            {
                usage.ProcessUsage = Round(_processUsage);

                if (Enabled)
                {
                    usage.Types = _types.Select(o => new TrakHoundCpuTypeUsage { Type = o.Key, Usage = Round(o.Value.Usage), CpuTime = Math.Round(o.Value.TotalProcessorTime.TotalMilliseconds) }).ToList();
                    usage.UnattributedUsage = Round(GetUnattributed(_processUsage, _types.Values.Sum(o => o.Usage)));
                    usage.Modules = _modules.Values
                        .Select(o => new TrakHoundCpuModuleUsage { Type = o.Type, Id = o.Id, Name = o.Name, Usage = Round(o.Monitor.Usage), CpuTime = Math.Round(o.Monitor.TotalProcessorTime.TotalMilliseconds) })
                        .OrderByDescending(o => o.Usage).ThenByDescending(o => o.CpuTime)
                        .ToList();
                }

                if (includeHistory) usage.History = _history.ToList();
            }

            return usage;
        }


        private void Sample(object state)
        {
            // Skip if the previous Sample is still running
            if (Interlocked.Exchange(ref _sampling, 1) == 1) return;

            try
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);

                TimeSpan processorTime;
                try { processorTime = _process.TotalProcessorTime; } catch { processorTime = _lastProcessorTime; }

                double processUsage = 0;
                if (elapsed > TimeSpan.Zero)
                {
                    processUsage = Math.Clamp((processorTime - _lastProcessorTime).Ticks / (double)(elapsed.Ticks * Environment.ProcessorCount) * 100, 0, 100);
                }

                _lastProcessorTime = processorTime;
                _lastTimestamp = now;

                lock (_lock)
                {
                    _processUsage = processUsage;

                    foreach (var monitor in _types.Values) monitor.Sample(0);
                    foreach (var module in _modules.Values)
                    {
                        module.Monitor.Sample(0);
                        foreach (var child in module.Children.Values) child.Sample(0);
                    }

                    var sample = new TrakHoundCpuUsageSample
                    {
                        Timestamp = DateTime.UtcNow,
                        Process = Round(processUsage),
                        Types = _types.ToDictionary(o => o.Key, o => Round(o.Value.Usage)),
                        Unattributed = Round(GetUnattributed(processUsage, _types.Values.Sum(o => o.Usage)))
                    };

                    _history.Enqueue(sample);
                    while (_history.Count > 0 && sample.Timestamp - _history.Peek().Timestamp > HistoryDuration) _history.Dequeue();
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _sampling, 0);
            }
        }

        // The Module Types and the Process are sampled at slightly different moments, so the measured total can exceed the Process for a sample
        private static double GetUnattributed(double process, double measured) => Math.Max(0, process - measured);

        private static double Round(double value) => Math.Round(value, 2);
    }
}
