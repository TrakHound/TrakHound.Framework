// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Buffers;

namespace TrakHound.Clients
{
    /// <summary>
    /// Reads Metrics directly from the Operation Buffers running in this process
    /// </summary>
    public class TrakHoundInstanceBuffersClient : ITrakHoundBuffersClient
    {
        public Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics()
        {
            return Task.FromResult(TrakHoundBufferRegistry.GetMetrics());
        }

        public Task<TrakHoundBufferMetrics> GetMetrics(string bufferId)
        {
            return Task.FromResult(TrakHoundBufferRegistry.GetMetrics(bufferId));
        }

        public Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics(IEnumerable<string> bufferIds)
        {
            return Task.FromResult(TrakHoundBufferRegistry.GetMetrics(bufferIds));
        }
    }
}
