// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Buffers;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query the Operation Buffers of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundBuffersClient
    {
        /// <summary>
        /// Gets the Metrics for all Operation Buffers
        /// </summary>
        Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics();

        /// <summary>
        /// Gets the Metrics for the specified Operation Buffer
        /// </summary>
        Task<TrakHoundBufferMetrics> GetMetrics(string bufferId);

        /// <summary>
        /// Gets the Metrics for the specified Operation Buffers
        /// </summary>
        Task<IEnumerable<TrakHoundBufferMetrics>> GetMetrics(IEnumerable<string> bufferIds);
    }
}
