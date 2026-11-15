// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceBlobServiceClient : ITrakHoundBlobServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceBlobServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Blobs.CheckAvailability(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Blobs.ListCollections(cancellationToken: cancellationToken);
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Blobs.ListKeys(collection, skip, take, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Blobs.List(collection, skip, take, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Blobs.Read(collection, key, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var response = await router.Blobs.Download(collection, key, cancellationToken: cancellationToken);
                    if (response.Success && response.Result != null && response.Result.CanSeek)
                    {
                        if (response.Result.Position > 0)
                        {
                            response.Result.Seek(0, SeekOrigin.Begin);
                        }
                    }
                    return response;
                }
            }

            return default;
        }

        /// <summary>
        /// Downloads from the Service that answered a previous request for the Blob (ex. the Read for its Content Type), so the content
        /// matches that Blob and the Route's Targets aren't tried again. Uses the Router when the Service has no Direct Router.
        /// </summary>
        internal async Task<TrakHoundServiceResponse<Stream>> DownloadFromService(string serviceId, string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key))
            {
                // Also enters the caller's Zone, so the Service call is still checked against it
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var serviceRouter = _baseClient.Instance.ServicesProvider.Routers.GetDirectRouter(serviceId) ?? router;

                    var response = await serviceRouter.Blobs.Download(collection, key, cancellationToken: cancellationToken);
                    if (response.Success && response.Result != null && response.Result.CanSeek)
                    {
                        if (response.Result.Position > 0)
                        {
                            response.Result.Seek(0, SeekOrigin.Begin);
                        }
                    }
                    return response;
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType = "application/octet-stream", IReadOnlyDictionary<string, string> metadata = null, bool overwrite = true, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Blobs.Upload(collection, key, content, contentType, metadata, overwrite, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Blobs.Delete(collection, key, cancellationToken: cancellationToken);
                }
            }

            return default;
        }
    }
}
