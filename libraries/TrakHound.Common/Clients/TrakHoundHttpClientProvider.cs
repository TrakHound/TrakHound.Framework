// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;

namespace TrakHound.Clients
{
    /// <summary>
    /// ITrakHoundClientProvider that creates TrakHoundHttpClients using a shared Client Configuration
    /// </summary>
    public class TrakHoundHttpClientProvider : ITrakHoundClientProvider
    {
        private readonly TrakHoundHttpClientConfiguration _clientConfiguration;


        /// <summary>
        /// The Configuration used to connect each Client to the Instance
        /// </summary>
        public TrakHoundHttpClientConfiguration ClientConfiguration => _clientConfiguration;


        /// <summary>
        /// Creates a new Client Provider that uses the specified Client Configuration
        /// </summary>
        public TrakHoundHttpClientProvider(TrakHoundHttpClientConfiguration clientConfiguration)
        {
            _clientConfiguration = clientConfiguration;
        }

        /// <inheritdoc/>
        public ITrakHoundClient GetClient(string routerId = null)
        {
            return new TrakHoundHttpClient(_clientConfiguration, routerId);
        }

        /// <summary>
        /// Gets the Information for each Service Router configured on the Instance (empty if none)
        /// </summary>
        public async Task<IEnumerable<ITrakHoundServiceRouterInformation>> GetRouters()
        {
            var url = Url.Combine(_clientConfiguration.GetHttpBaseUrl(), TrakHoundHttpConstants.ServiceRoutersPrefix);

            var response = await new TrakHoundHttpRequestSender(_clientConfiguration).Get<IEnumerable<TrakHoundServiceRouterInformation>>(url);
            if (!response.IsNullOrEmpty())
            {
                var routerInformations = new List<ITrakHoundServiceRouterInformation>();
                foreach (var x in response) routerInformations.Add(x);
                return routerInformations;
            }

            return Enumerable.Empty<ITrakHoundServiceRouterInformation>();
        }
    }
}
