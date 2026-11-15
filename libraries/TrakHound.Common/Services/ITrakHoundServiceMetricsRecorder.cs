// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Services
{
    /// <summary>
    /// Receives the result of each request a <see cref="TrakHoundServiceRouter"/> sends to a Service
    /// </summary>
    public interface ITrakHoundServiceMetricsRecorder
    {
        /// <summary>
        /// Record a request that was handled by a Service. Requests the Service does not support (Status Code 405) are not recorded.
        /// Called on the request path, so implementations should be fast and must not throw.
        /// </summary>
        /// <param name="routerId">The Router that sent the request</param>
        /// <param name="service">The Service that handled the request</param>
        /// <param name="serviceType">The Service interface the request was for (ex. ITrakHoundTableService)</param>
        /// <param name="operation">The name of the request (ex. Query)</param>
        /// <param name="statusCode">The Status Code returned by the Service (500 if the Service threw an Exception)</param>
        /// <param name="message">The Message returned by the Service</param>
        /// <param name="duration">How long the Service took to respond</param>
        void Record(string routerId, ITrakHoundService service, Type serviceType, string operation, int statusCode, string message, TimeSpan duration);
    }
}
