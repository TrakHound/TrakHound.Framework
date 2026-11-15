// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Tracks all active Operation Buffers in the process (so their Metrics can be read).
    /// Buffers register themselves when created and unregister when Disposed
    /// </summary>
    public static class TrakHoundBufferRegistry
    {
        private static readonly ConcurrentDictionary<string, ITrakHoundOperationBuffer> _buffers = new ConcurrentDictionary<string, ITrakHoundOperationBuffer>();


        /// <summary>
        /// Registers the Buffer by its Id (replacing any Buffer already registered with the same Id). Buffers without an Id are ignored
        /// </summary>
        public static void Register(ITrakHoundOperationBuffer buffer)
        {
            if (buffer != null && !string.IsNullOrEmpty(buffer.Id))
            {
                _buffers[buffer.Id] = buffer;
            }
        }

        /// <summary>
        /// Unregisters the Buffer (only if it is still the instance registered for its Id)
        /// </summary>
        public static void Unregister(ITrakHoundOperationBuffer buffer)
        {
            if (buffer != null && !string.IsNullOrEmpty(buffer.Id))
            {
                // Only remove if this is still the registered instance (a newer Buffer may have replaced it)
                _buffers.TryRemove(new KeyValuePair<string, ITrakHoundOperationBuffer>(buffer.Id, buffer));
            }
        }


        /// <summary>
        /// Gets the Metrics for all registered Buffers (ordered by BufferId)
        /// </summary>
        public static IEnumerable<TrakHoundBufferMetrics> GetMetrics()
        {
            return _buffers.Values
                .Select(o => o.Metrics)
                .Where(o => o != null)
                .OrderBy(o => o.BufferId)
                .ToList();
        }

        /// <summary>
        /// Gets the Metrics for the specified Buffer (null if not found)
        /// </summary>
        public static TrakHoundBufferMetrics GetMetrics(string bufferId)
        {
            if (!string.IsNullOrEmpty(bufferId) && _buffers.TryGetValue(bufferId, out var buffer))
            {
                return buffer.Metrics;
            }

            return null;
        }

        /// <summary>
        /// Gets the Metrics for the specified Buffers (Buffers not found are skipped)
        /// </summary>
        public static IEnumerable<TrakHoundBufferMetrics> GetMetrics(IEnumerable<string> bufferIds)
        {
            if (bufferIds.IsNullOrEmpty()) return Enumerable.Empty<TrakHoundBufferMetrics>();

            return bufferIds
                .Distinct()
                .Select(GetMetrics)
                .Where(o => o != null)
                .ToList();
        }
    }
}
