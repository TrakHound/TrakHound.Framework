// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Blobs
{
    /// <summary>
    /// Routes Blob Requests to the Service and Router Targets configured for the "Blobs" Route of a Service Router
    /// </summary>
    public class TrakHoundBlobRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a Blob Router for the specified Service Router
        /// </summary>
        public TrakHoundBlobRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }

        /// <summary>
        /// Checks the availability of the Blob Services matched by the Router
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.CheckAvailability(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundServiceAvailabilityResponse>, Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.CheckAvailability(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("CheckAvailability", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Lists the Blob Collections and the number of Blobs in each
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, IEnumerable<TrakHoundBlobCollection>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.ListCollections(cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<TrakHoundBlobCollection>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.ListCollections(routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("ListCollections", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Lists the Keys of the Blobs in the specified Collection (paged using skip and take)
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.ListKeys(collection, skip, take, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<string>>, Task<TrakHoundServiceResponse<IEnumerable<string>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.ListKeys(collection, skip, take, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("ListKeys", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Lists the Blobs (without content) in the specified Collection (paged using skip and take)
        /// </summary>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, IEnumerable<TrakHoundBlob>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.List(collection, skip, take, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<IEnumerable<TrakHoundBlob>>, Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.List(collection, skip, take, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("List", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Reads the information (without content) of the Blob with the specified Collection and Key
        /// </summary>
        public async Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, TrakHoundBlob>, Task<TrakHoundServiceResponse<TrakHoundBlob>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Read(collection, key, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<TrakHoundBlob>, Task<TrakHoundServiceResponse<TrakHoundBlob>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.Read(collection, key, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Read", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Opens a Stream to read the content of the Blob with the specified Collection and Key
        /// </summary>
        public async Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetDriverRequest<ITrakHoundBlobService, Stream>, Task<TrakHoundServiceResponse<Stream>>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Download(collection, key, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest<Stream>, Task<TrakHoundServiceResponse<Stream>>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.Download(collection, key, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Download", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Uploads content to the Blob with the specified Collection and Key
        /// (when overwrite is false, an existing Blob is not replaced)
        /// </summary>
        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, bool overwrite = true, string requestId = null, CancellationToken cancellationToken = default)
        {
            // A fallback Target is only given the content from its start (see RouteContent)
            var routeContent = new RouteContent(content);

            // Set Query Driver Command
            Func<RouteTargetServiceRequest<ITrakHoundBlobService>, Task<TrakHoundServiceResponse>> serviceCommand = async (serviceRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable();
                return await serviceRequest.Service.Upload(collection, key, routeContent.Stream, contentType, metadata, overwrite, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = async (routerRequest) =>
            {
                if (!routeContent.TryBegin()) return RouteContent.NotResendable();
                return await routerRequest.Router.Blobs.Upload(collection, key, routeContent.Stream, contentType, metadata, overwrite, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Upload", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }

        /// <summary>
        /// Deletes the Blob with the specified Collection and Key
        /// </summary>
        public async Task<TrakHoundServiceResponse> Delete(string collection, string key, string requestId = null, CancellationToken cancellationToken = default)
        {
            // Set Query Driver Command
            Func<RouteTargetServiceRequest<ITrakHoundBlobService>, Task<TrakHoundServiceResponse>> serviceCommand = async (serviceRequest) =>
            {
                return await serviceRequest.Service.Delete(collection, key, cancellationToken);
            };

            // Set Query Router Command
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = async (routerRequest) =>
            {
                return await routerRequest.Router.Blobs.Delete(collection, key, routerRequest.Request.Id, cancellationToken);
            };


            var request = new RouteRequest("Delete", requestId) { CancellationToken = cancellationToken };

            // Run Targets
            var response = await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundBlobRoutes.Blobs),
                serviceCommand,
                routerCommand
                );

            return response;
        }
    }
}
