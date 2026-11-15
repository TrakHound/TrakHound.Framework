// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceServiceRoutersClient : ITrakHoundServiceRoutersClient
    {
        private readonly TrakHoundServiceRouterProvider _routerProvider;


        public TrakHoundInstanceServiceRoutersClient(TrakHoundServiceRouterProvider routerProvider)
        {
            _routerProvider = routerProvider;
        }


		public async Task<IEnumerable<ITrakHoundServiceRouterInformation>> GetRouters()
        {
            var informations = _routerProvider.GetInformation();
            if (!informations.IsNullOrEmpty())
            {
                var results = new List<ITrakHoundServiceRouterInformation>();
                foreach (var information in informations)
                {
                    results.Add(information);
                }
                return results;
            }

            return null;
        }

        public async Task<ITrakHoundServiceRouterInformation> GetRouter(string routerId)
        {
            return _routerProvider.GetInformation(routerId);
        }
    }
}
