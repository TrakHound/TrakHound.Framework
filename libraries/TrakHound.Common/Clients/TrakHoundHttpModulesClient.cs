// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Modules;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundModulesClient that queries the Modules Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpModulesClient : TrakHoundHttpClientBase, ITrakHoundModulesClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.ModulesPrefix;


        /// <summary>
        /// Creates a new Modules Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpModulesClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundModuleInformation>> GetModules()
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute);
            return await Requests.Get<IEnumerable<TrakHoundModuleInformation>>(url);
        }
    }
}
