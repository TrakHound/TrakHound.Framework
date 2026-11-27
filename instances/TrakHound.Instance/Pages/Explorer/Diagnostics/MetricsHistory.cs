// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Explorer.Diagnostics
{
    /// <summary>
    /// A Sample charted by a MetricsChart, kept in a MetricsHistory
    /// </summary>
    public interface IMetricsSample
    {
        /// <summary>
        /// The time (UTC) the Sample was read
        /// </summary>
        DateTime Timestamp { get; }
    }

    /// <summary>
    /// The Samples charted by a metrics page, by key (ex. the Instance ID and the item charted). A page keeps them in a static field,
    /// so the charts are already drawn when the page is opened again, and everyone who has the page open adds to the same Samples.
    /// Samples are only added while a page is open.
    /// </summary>
    public class MetricsHistory<TSample> where TSample : class, IMetricsSample
    {
        // Several viewers add to the same Samples, so a Sample read right after another is left out. The pages read every second,
        // so this keeps one Sample about every second however the viewers' reads line up
        private static readonly TimeSpan _minimumInterval = TimeSpan.FromMilliseconds(800);

        // How often the keys that haven't had a Sample for the whole Window are looked for
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(30);

        private readonly Dictionary<string, Queue<TSample>> _samples = new Dictionary<string, Queue<TSample>>();
        private readonly object _lock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;


        /// <summary>
        /// How far back the Samples go
        /// </summary>
        public TimeSpan Window { get; }


        public MetricsHistory(TimeSpan window)
        {
            Window = window;
        }


        /// <summary>
        /// Adds a Sample (unless one was added for the key less than 0.8 seconds before it)
        /// </summary>
        public void Add(string key, TSample sample)
        {
            if (key == null || sample == null) return;

            lock (_lock)
            {
                if (!_samples.TryGetValue(key, out var samples))
                {
                    samples = new Queue<TSample>();
                    _samples.Add(key, samples);
                }

                if (samples.Count > 0 && sample.Timestamp - samples.Last().Timestamp < _minimumInterval) return;

                samples.Enqueue(sample);
                while (samples.Count > 0 && sample.Timestamp - samples.Peek().Timestamp > Window) samples.Dequeue();

                // The items that are gone (ex. a Buffer that was removed) are left out after the Window
                if (sample.Timestamp - _lastCleanup >= _cleanupInterval)
                {
                    _lastCleanup = sample.Timestamp;
                    foreach (var pair in _samples.Where(o => o.Value.Count == 0 || sample.Timestamp - o.Value.Last().Timestamp > Window).ToList())
                    {
                        _samples.Remove(pair.Key);
                    }
                }
            }
        }

        /// <summary>
        /// The Samples for a key, oldest first (a new array, so a chart sees that they changed)
        /// </summary>
        public TSample[] Get(string key)
        {
            lock (_lock)
            {
                return key != null && _samples.TryGetValue(key, out var samples) ? samples.ToArray() : Array.Empty<TSample>();
            }
        }

        /// <summary>
        /// The latest Sample for a key, or null when there is none
        /// </summary>
        public TSample GetLatest(string key)
        {
            lock (_lock)
            {
                return key != null && _samples.TryGetValue(key, out var samples) && samples.Count > 0 ? samples.Last() : null;
            }
        }

        /// <summary>
        /// The key for an item charted for an Instance
        /// </summary>
        public static string GetKey(string instanceId, params string[] items) => string.Join("\0", new[] { instanceId ?? "" }.Concat(items ?? Array.Empty<string>()));
    }
}
