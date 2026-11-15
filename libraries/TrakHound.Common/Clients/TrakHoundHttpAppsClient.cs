// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Apps;
using TrakHound.Http;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    internal class TrakHoundHttpAppsClient : ITrakHoundAppsClient
    {
        private readonly TrakHoundHttpClient _baseClient;


        public string BaseUrl => _baseClient != null ? _baseClient.HttpBaseUrl : null;

        public string RouterId => _baseClient != null ? _baseClient.RouterId : null;


        public TrakHoundHttpAppsClient(TrakHoundHttpClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<IEnumerable<TrakHoundAppInformation>> GetInformation()
        {
            var url = Url.Combine(BaseUrl, "_apps/information");
            return await _baseClient.Requests.Get<IEnumerable<TrakHoundAppInformation>>(url);
        }

        public async Task<TrakHoundAppInformation> GetInformation(string appId)
        {
            var url = Url.Combine(BaseUrl, "_apps/information");
            url = Url.Combine(url, appId);

            return await _baseClient.Requests.Get<TrakHoundAppInformation>(url);
        }

        public async Task<bool> ReloadConfiguration(string appId)
        {
            var url = Url.Combine(BaseUrl, "_apps/reload");
            url = Url.Combine(url, appId);

            var response = await _baseClient.Requests.PostResponse(url, string.Empty);
            return response.Success;
        }

        public async Task<bool> RefreshApp(string appId)
        {
            var url = Url.Combine(BaseUrl, "_apps/refresh/app");
            url = Url.Combine(url, appId);

            var response = await _baseClient.Requests.PostResponse(url, string.Empty);
            return response.Success;
        }

        public async Task<bool> RefreshConnection(string connectionId)
        {
            var url = Url.Combine(BaseUrl, "_apps/refresh/connection");
            url = Url.Combine(url, connectionId);

            var response = await _baseClient.Requests.PostResponse(url, string.Empty);
            return response.Success;
        }


        public async Task<IEnumerable<TrakHoundAppMetrics>> GetMetrics()
        {
            var url = Url.Combine(BaseUrl, "_apps/metrics");
            return await _baseClient.Requests.Get<IEnumerable<TrakHoundAppMetrics>>(url);
        }

        public async Task<TrakHoundAppMetrics> GetMetrics(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;

            var url = Url.Combine(BaseUrl, "_apps/metrics");
            url = Url.Combine(url, appId);

            return await _baseClient.Requests.Get<TrakHoundAppMetrics>(url);
        }

        public async Task<IEnumerable<TrakHoundAppConnectionMetrics>> GetConnectionMetrics(string appId = null)
        {
            var url = Url.Combine(BaseUrl, "_apps/metrics/connections");
            url = Url.AddQueryParameter(url, "appId", appId);

            return await _baseClient.Requests.Get<IEnumerable<TrakHoundAppConnectionMetrics>>(url);
        }


        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string appId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            return null;
        }
    }
}
