// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;

namespace TrakHound
{
    /// <summary>
    /// A single CPU and Memory measurement for the Instance process
    /// </summary>
    public class TrakHoundProcessSample
    {
        /// <summary>
        /// The time (UTC) the sample was taken
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The CPU usage (percent of total machine capacity, 0 - 100)
        /// </summary>
        public double CpuUsage { get; set; }

        /// <summary>
        /// The physical memory used by the process (Working Set) in bytes
        /// </summary>
        public long WorkingSet { get; set; }

        /// <summary>
        /// The memory allocated on the managed (GC) Heap in bytes
        /// </summary>
        public long ManagedMemory { get; set; }

        /// <summary>
        /// The Working Set in Megabytes (used for Charts)
        /// </summary>
        public double WorkingSetMB => WorkingSet / 1024d / 1024d;
    }

    /// <summary>
    /// Samples the total CPU and Memory usage of the Instance process and keeps a short history
    /// </summary>
    public class TrakHoundProcessMonitor : IDisposable
    {
        private static readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(1);
        private static readonly Lazy<TrakHoundProcessMonitor> _default = new Lazy<TrakHoundProcessMonitor>(() => new TrakHoundProcessMonitor());

        /// <summary>
        /// The shared Monitor for the current process
        /// </summary>
        public static TrakHoundProcessMonitor Default => _default.Value;

        /// <summary>
        /// How long the sample history is kept for
        /// </summary>
        public static readonly TimeSpan HistoryDuration = TimeSpan.FromMinutes(10);

        private readonly Process _process = Process.GetCurrentProcess();
        private readonly Queue<TrakHoundProcessSample> _history = new Queue<TrakHoundProcessSample>();
        private readonly Timer _timer;
        private readonly object _lock = new object();
        private TimeSpan _lastProcessorTime;
        private long _lastTimestamp;
        private TrakHoundProcessSample _current;
        private int _sampling;


        /// <summary>
        /// The most recent sample
        /// </summary>
        public TrakHoundProcessSample Current
        {
            get { lock (_lock) return _current; }
        }

        /// <summary>
        /// The time the Instance process started (UTC)
        /// </summary>
        public DateTime StartTime { get; }

        /// <summary>
        /// The number of logical processors on the machine
        /// </summary>
        public int ProcessorCount => Environment.ProcessorCount;

        /// <summary>
        /// The total physical memory available to the process (bytes)
        /// </summary>
        public long TotalAvailableMemory => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        /// <summary>
        /// The number of threads in the Instance process
        /// </summary>
        public int ThreadCount
        {
            get
            {
                try { return _process.Threads.Count; }
                catch { return 0; }
            }
        }


        private TrakHoundProcessMonitor()
        {
            try { StartTime = _process.StartTime.ToUniversalTime(); }
            catch { StartTime = DateTime.UtcNow; }

            _lastProcessorTime = _process.TotalProcessorTime;
            _lastTimestamp = Stopwatch.GetTimestamp();

            _timer = new Timer(Sample, null, _sampleInterval, _sampleInterval);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _process.Dispose();
        }


        /// <summary>
        /// Get the sample history (oldest first). If 'from' is specified, only samples after that time (UTC) are returned.
        /// </summary>
        public IEnumerable<TrakHoundProcessSample> GetHistory(DateTime? from = null)
        {
            lock (_lock)
            {
                if (from.HasValue) return _history.Where(o => o.Timestamp > from.Value).ToList();
                return _history.ToList();
            }
        }

        private void Sample(object state)
        {
            // Skip if the previous Sample is still running
            if (Interlocked.Exchange(ref _sampling, 1) == 1) return;

            try
            {
                _process.Refresh();

                var now = Stopwatch.GetTimestamp();
                var processorTime = _process.TotalProcessorTime;
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);

                double cpuUsage = 0;
                if (elapsed > TimeSpan.Zero)
                {
                    var cpuTicks = (processorTime - _lastProcessorTime).Ticks;
                    cpuUsage = Math.Clamp(cpuTicks / (double)(elapsed.Ticks * Environment.ProcessorCount) * 100, 0, 100);
                }

                _lastProcessorTime = processorTime;
                _lastTimestamp = now;

                var sample = new TrakHoundProcessSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsage = cpuUsage,
                    WorkingSet = _process.WorkingSet64,
                    ManagedMemory = GC.GetTotalMemory(false)
                };

                lock (_lock)
                {
                    _current = sample;
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
    }
}
