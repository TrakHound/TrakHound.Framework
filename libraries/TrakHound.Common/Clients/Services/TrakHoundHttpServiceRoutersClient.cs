// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundServiceRoutersClient that uses the Service Routers Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpServiceRoutersClient : TrakHoundHttpClientBase, ITrakHoundServiceRoutersClient
    {
        /// <summary>
        /// Creates a new Service Routers Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpServiceRoutersClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<ITrakHoundServiceRouterInformation>> GetRouters()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceRoutersPrefix);

            return await Requests.Get<IEnumerable<TrakHoundServiceRouterInformation>>(url);
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundServiceRouterInformation> GetRouter(string routerId)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.ServiceRoutersPrefix, routerId);

            return await Requests.Get<TrakHoundServiceRouterInformation>(url);
        }
    }
}
