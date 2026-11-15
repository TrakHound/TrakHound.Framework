// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Base interface for the Service Clients (Blobs, KeyValues, Messages, Message Queues, Tables, Time Series)
    /// </summary>
    public interface ITrakHoundServiceClient
    {
        /// <summary>
        /// Checks whether the Service is available through the specified Router (null uses the Client's Router)
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default);
    }
}
