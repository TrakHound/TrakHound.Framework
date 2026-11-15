// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Packages;

namespace TrakHound.Api
{
    struct ApiRouteConfiguration
    {
        public string Route { get; set; }

        public TrakHoundPackage Package { get; set; }

        public string VolumeId { get; set; }

        public string PackageReadMe { get; set; }

        public ITrakHoundApiConfiguration Configuration { get; set; }

        public IEnumerable<TrakHoundApiController> Controllers { get; set; }

        /// <summary>
        /// The Api's Information (built once when the Route is loaded, since it is read on every Request)
        /// </summary>
        public TrakHoundApiInformation Information { get; set; }

        /// <summary>
        /// Changes when the Configuration or Package changes (computed once, since both Hashes are computed on every read)
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The Endpoint Routes of the Controllers, sorted in the order they are matched
        /// </summary>
        internal TrakHoundApiProvider.MethodRoute[] MethodRoutes { get; set; }

        /// <summary>
        /// The full Route of each Endpoint (including the Api Route), used to check Requests for an Api at the root
        /// </summary>
        internal string[] EndpointRoutes { get; set; }


        public ApiRouteConfiguration(
            string route,
            TrakHoundPackage package,
            ITrakHoundApiConfiguration configuration,
            IEnumerable<TrakHoundApiController> controllers,
            string volumeId,
            string packageReadMe = null
            )
        {
            Route = route;
            Package= package;
            Configuration= configuration;
            Controllers = controllers;
            VolumeId = volumeId;
            PackageReadMe = packageReadMe;
        }
    }
}
