// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundServiceConfigurationsClient that uses the Service Configurations Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpServiceConfigurationsClient : TrakHoundHttpClientBase, ITrakHoundServiceConfigurationsClient
    {
        private const string ServicesRoute = "services";
        private const string RoutersRoute = "routers";


        /// <summary>
        /// Creates a new Service Configurations Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpServiceConfigurationsClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundServiceConfiguration>> GetServices()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, ServicesRoute);

            return await Requests.Get<IEnumerable<TrakHoundServiceConfiguration>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceConfiguration> GetService(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return null;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, ServicesRoute, configurationId);

            return await Requests.Get<TrakHoundServiceConfiguration>(url);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveService(TrakHoundServiceConfiguration configuration)
        {
            if (configuration == null) return false;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, ServicesRoute);

            var response = await Requests.PutResponse(url, configuration);
            return response.Success;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveService(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return false;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, ServicesRoute, configurationId);

            var response = await Requests.DeleteResponse(url);
            return response.Success;
        }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundServiceRouterConfiguration>> GetRouters()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, RoutersRoute);

            return await Requests.Get<IEnumerable<TrakHoundServiceRouterConfiguration>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceRouterConfiguration> GetRouter(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return null;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, RoutersRoute, configurationId);

            return await Requests.Get<TrakHoundServiceRouterConfiguration>(url);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveRouter(TrakHoundServiceRouterConfiguration configuration)
        {
            if (configuration == null) return false;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, RoutersRoute);

            var response = await Requests.PutResponse(url, configuration);
            return response.Success;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveRouter(string configurationId)
        {
            if (string.IsNullOrEmpty(configurationId)) return false;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceConfigurationsPrefix, RoutersRoute, configurationId);

            var response = await Requests.DeleteResponse(url);
            return response.Success;
        }
    }
}
