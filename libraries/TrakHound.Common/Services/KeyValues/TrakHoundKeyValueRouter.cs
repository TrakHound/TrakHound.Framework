// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.KeyValues
{
    /// <summary>
    /// Routes KeyValue Requests to the KeyValue Services and Routers configured as Targets for the Router
    /// </summary>
    public class TrakHoundKeyValueRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a KeyValue Router that uses the Targets of the specified Service Router
        /// </summary>
        public TrakHoundKeyValueRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }


        /// <summary>
        /// Checks the availability of the KeyValue Targets
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundKeyValueService, TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.CheckAvailability(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.CheckAvailability(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("CheckAvailability", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Lists the KeyValue Collections available through the Targets
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundKeyValueService, IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.ListCollections(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.ListCollections(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("ListCollections", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Lists the Keys in the specified Collection that match the pattern ("*" wildcard), paged using skip and take
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundKeyValueService, IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.ListKeys(collection, pattern, skip, take, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.ListKeys(collection, pattern, skip, take, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("ListKeys", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Reads the Values for the specified Keys in the Collection
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundKeyValueService, TrakHoundKeyValueResponse>, Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Read(collection, keys, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundKeyValueResponse>, Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.Read(collection, keys, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Read", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Gets the number of Keys in the specified Collection
        /// </summary>
        public async Task<TrakHoundServiceResponse<int>> Count(string collection, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundKeyValueService, int>, Task<TrakHoundServiceResponse<int>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Count(collection, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<int>, Task<TrakHoundServiceResponse<int>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.Count(collection, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Count", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Adds or updates each of the Key Value entries in the Collection, with an optional time to live (ttl) if supported by the Service
        /// </summary>
        public async Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetServiceRequest<ITrakHoundKeyValueService>, Task<TrakHoundServiceResponse>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Publish(collection, entries, ttl, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.Publish(collection, entries, ttl, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Publish", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Deletes the specified Keys from the Collection
        /// </summary>
        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetServiceRequest<ITrakHoundKeyValueService>, Task<TrakHoundServiceResponse>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Delete(collection, keys, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.KeyValues.Delete(collection, keys, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Delete", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundKeyValueRoutes.KeyValues),
                serviceCommand,
                routerCommand
                );

            return response;
        }
    }
}
