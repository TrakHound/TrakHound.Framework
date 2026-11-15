// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceServiceDriversClient : ITrakHoundServiceDriversClient
    {
        private readonly TrakHoundServiceProvider _driverProvider;
        private readonly TrakHoundServiceMetricsCollector _metrics;


        public TrakHoundInstanceServiceDriversClient(TrakHoundServiceProvider driverProvider, TrakHoundServiceMetricsCollector metrics = null)
        {
            _driverProvider = driverProvider;
            _metrics = metrics;
        }


		public async Task<IEnumerable<TrakHoundServiceInformation>> GetDrivers()
        {
            var informations = _driverProvider.GetInformation();
            if (!informations.IsNullOrEmpty())
            {
                var results = new List<TrakHoundServiceInformation>();
                foreach (var information in informations)
                {
                    results.Add(information);
                }
                return results;
            }

            return null;
        }

        public async Task<TrakHoundServiceInformation> GetDriver(string configurationId)
        {
            return _driverProvider.GetInformation(configurationId);
        }


        public Task<IEnumerable<TrakHoundServiceMetrics>> GetMetrics()
        {
            return Task.FromResult(_metrics?.GetMetrics());
        }

        public Task<TrakHoundServiceMetrics> GetMetrics(string configurationId)
        {
            return Task.FromResult(_metrics?.GetMetrics(configurationId));
        }
    }
}
