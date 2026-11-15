// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundServiceDriversClient that uses the Services Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpServiceDriversClient : TrakHoundHttpClientBase, ITrakHoundServiceDriversClient
    {
        /// <summary>
        /// Creates a new Service Drivers Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpServiceDriversClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundServiceInformation>> GetDrivers()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServicesPrefix);

            return await Requests.Get<IEnumerable<TrakHoundServiceInformation>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceInformation> GetDriver(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return null;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServicesPrefix, configurationId);

            return await Requests.Get<TrakHoundServiceInformation>(url);
        }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundServiceMetrics>> GetMetrics()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServicesPrefix, "metrics");

            return await Requests.Get<IEnumerable<TrakHoundServiceMetrics>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceMetrics> GetMetrics(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return null;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServicesPrefix, "metrics", configurationId);

            return await Requests.Get<TrakHoundServiceMetrics>(url);
        }
    }
}
