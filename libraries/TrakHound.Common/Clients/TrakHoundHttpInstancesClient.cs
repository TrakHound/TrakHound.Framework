// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Instances;

namespace TrakHound.Clients
{
    internal class TrakHoundHttpInstancesClient : ITrakHoundInstancesClient
    {
        private readonly TrakHoundHttpClient _baseClient;


        public TrakHoundHttpInstancesClient(TrakHoundHttpClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundInstanceInformation> GetHostInformation()
        {
            var url = Url.Combine(_baseClient.HttpBaseUrl, TrakHoundHttpConstants.InstancesPrefix);
            url = Url.Combine(url, "information", "host");

            return await _baseClient.Requests.Get<TrakHoundInstanceInformation>(url);
        }

        public async Task<TrakHoundInstanceHealth> GetHealth()
        {
            var url = Url.Combine(_baseClient.HttpBaseUrl, TrakHoundHttpConstants.InstancesPrefix, "health");

            return await _baseClient.Requests.Get<TrakHoundInstanceHealth>(url);
        }

        public async Task<TrakHoundCpuUsage> GetCpuUsage()
        {
            var url = Url.Combine(_baseClient.HttpBaseUrl, TrakHoundHttpConstants.InstancesPrefix, "cpu");

            return await _baseClient.Requests.Get<TrakHoundCpuUsage>(url);
        }

        public async Task<IEnumerable<TrakHoundInstanceBoot>> GetBoots(int take = 100)
        {
            var url = Url.Combine(_baseClient.HttpBaseUrl, TrakHoundHttpConstants.InstancesPrefix, "boots");
            url = Url.AddQueryParameter(url, "take", take);

            return await _baseClient.Requests.Get<IEnumerable<TrakHoundInstanceBoot>>(url);
        }

        public async Task<TrakHoundInstanceBoot> GetBoot(string bootId)
        {
            if (string.IsNullOrEmpty(bootId)) return null;

            var url = Url.Combine(_baseClient.HttpBaseUrl, TrakHoundHttpConstants.InstancesPrefix, "boots", bootId);

            return await _baseClient.Requests.Get<TrakHoundInstanceBoot>(url);
        }

        public Task<ITrakHoundStream<TrakHoundInstanceInformation>> SubscribeInformation(string instanceId) => null; // Not Implemented

        public Task Start(string instanceId) => Task.CompletedTask; // Not Implemented

        public Task Stop(string instanceId) => Task.CompletedTask; // Not Implemented
    }
}
