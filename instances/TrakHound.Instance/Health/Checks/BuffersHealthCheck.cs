// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Buffers;
using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Operation Buffers whose Queue is nearly full, or whose backlog keeps growing (items are added faster than they are written)
    /// </summary>
    public class BuffersHealthCheck : ITrakHoundHealthCheck
    {
        private const double _queueFullRatio = 0.9;
        private const int _growingEvaluations = 6; // ~1 minute
        private const int _growingMinimum = 1000;

        private readonly Dictionary<string, Queue<int>> _history = new Dictionary<string, Queue<int>>(); // Buffer ID => recent Queue Item Counts
        private readonly object _lock = new object();


        public string Id => "buffers";

        public string Title => "Buffers";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var buffers = TrakHoundBufferRegistry.GetMetrics()?.Where(o => o != null && !string.IsNullOrEmpty(o.BufferId)).ToList();
            if (buffers.IsNullOrEmpty())
            {
                lock (_lock) _history.Clear();
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, "No active Buffers", "diagnostics/buffers"));
            }

            var items = new List<TrakHoundHealthCheckItem>();
            var totalQueued = 0L;

            lock (_lock)
            {
                // Forget Buffers that were removed
                foreach (var removed in _history.Keys.Except(buffers.Select(o => o.BufferId)).ToList()) _history.Remove(removed);

                foreach (var buffer in buffers)
                {
                    var queue = buffer.Queue;
                    if (queue == null) continue;

                    totalQueued += queue.ItemCount;

                    if (!_history.TryGetValue(buffer.BufferId, out var history))
                    {
                        history = new Queue<int>();
                        _history.Add(buffer.BufferId, history);
                    }

                    history.Enqueue(queue.ItemCount);
                    while (history.Count > _growingEvaluations) history.Dequeue();

                    string description = null;
                    if (queue.ItemLimit > 0 && queue.ItemCount >= queue.ItemLimit * _queueFullRatio)
                    {
                        description = $"Queue is {(double)queue.ItemCount / queue.ItemLimit:0%} full ({queue.ItemCount:N0} of {queue.ItemLimit:N0})";
                    }
                    else if (history.Count >= _growingEvaluations && queue.ItemCount >= _growingMinimum && IsGrowing(history))
                    {
                        description = $"Backlog growing : {history.Peek():N0} => {queue.ItemCount:N0} items over the last {_growingEvaluations * TrakHoundHealthMonitor.EvaluationInterval.TotalSeconds:0} seconds";
                    }

                    if (description != null)
                    {
                        items.Add(new TrakHoundHealthCheckItem
                        {
                            Id = buffer.BufferId,
                            Name = !string.IsNullOrEmpty(buffer.Name) ? buffer.Name : buffer.BufferId,
                            Status = TrakHoundHealthStatus.Degraded,
                            Description = description,
                            Link = "diagnostics/buffers"
                        });
                    }
                }
            }

            if (items.Count > 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, $"{items.Count} of {buffers.Count} Buffers falling behind", "diagnostics/buffers", items));
            }

            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, $"{buffers.Count} Buffers · {totalQueued:N0} items queued", "diagnostics/buffers"));
        }

        private static bool IsGrowing(IEnumerable<int> counts)
        {
            int? previous = null;
            foreach (var count in counts)
            {
                if (previous != null && count <= previous) return false;
                previous = count;
            }
            return true;
        }
    }
}
