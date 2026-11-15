// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to query the Services (Driver Configurations) loaded on the Instance
    /// </summary>
    public interface ITrakHoundServiceDriversClient
    {
        /// <summary>
        /// Gets the Information for each Service loaded on the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundServiceInformation>> GetDrivers();

        /// <summary>
        /// Gets the Information for the Service with the specified Configuration ID (null if not found)
        /// </summary>
        Task<TrakHoundServiceInformation> GetDriver(string configurationId);


        /// <summary>
        /// Gets the Request Metrics for each Service that has received requests
        /// </summary>
        Task<IEnumerable<TrakHoundServiceMetrics>> GetMetrics() => Task.FromResult<IEnumerable<TrakHoundServiceMetrics>>(null);

        /// <summary>
        /// Gets the Request Metrics for the specified Service (null if it has not received any requests)
        /// </summary>
        Task<TrakHoundServiceMetrics> GetMetrics(string configurationId) => Task.FromResult<TrakHoundServiceMetrics>(null);
    }
}
