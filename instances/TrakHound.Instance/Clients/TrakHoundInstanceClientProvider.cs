// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Functions;
using TrakHound.Instances;
using TrakHound.Services;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceClientProvider : ITrakHoundClientProvider
    {
        private readonly ITrakHoundInstance _hostInstance;
        private readonly ITrakHoundVolumeProvider _volumeProvider;


        public ITrakHoundApiProvider ApiProvider { get; set; }

        public ITrakHoundAppProvider AppProvider { get; set; }

        public TrakHoundFunctionManager FunctionManager { get; set; }

        public TrakHoundWorkerManager WorkerManager { get; set; }

        /// <summary>
        /// The current Services Provider (replaced by the Instance each time it is Started)
        /// </summary>
        public TrakHoundServicesProvider ServicesProvider { get; set; }


        public TrakHoundInstanceClientProvider(
            ITrakHoundInstance hostInstance, 
            TrakHoundServicesProvider servicesProvider, 
            ITrakHoundVolumeProvider volumeProvider
            )
        {
            _hostInstance = hostInstance;
            ServicesProvider = servicesProvider;
            _volumeProvider = volumeProvider;
        }


        public ITrakHoundClient GetClient(string routerId = null)
        {
            var client = new TrakHoundInstanceClient(_hostInstance, ApiProvider, AppProvider, FunctionManager, WorkerManager, ServicesProvider, _volumeProvider);
            client.RouterId = routerId;
            return client;
        }


        public async Task<IEnumerable<ITrakHoundServiceRouterInformation>> GetRouters()
        {
            return ServicesProvider?.Routers?.GetInformation() ?? Enumerable.Empty<ITrakHoundServiceRouterInformation>();
        }
    }
}
