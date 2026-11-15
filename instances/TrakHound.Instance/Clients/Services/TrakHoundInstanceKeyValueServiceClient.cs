// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceKeyValueServiceClient : ITrakHoundKeyValueServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceKeyValueServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.KeyValues.CheckAvailability(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                var response = await router.KeyValues.ListCollections(cancellationToken: cancellationToken);

                var outputResponse = new TrakHoundServiceResponse<IEnumerable<string>>();
                outputResponse.StatusCode = response.StatusCode;
                outputResponse.Message = response.Message;
                outputResponse.Result = response.Result;
                return outputResponse;
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(pattern))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var response = await router.KeyValues.ListKeys(collection, pattern, skip, take, cancellationToken: cancellationToken);

                    var outputResponse = new TrakHoundServiceResponse<IEnumerable<string>>();
                    outputResponse.StatusCode = response.StatusCode;
                    outputResponse.Message = response.Message;
                    outputResponse.Result = response.Result;
                    return outputResponse;
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<string>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var keys = new string[] { key };

                    var response = await router.KeyValues.Read(collection, keys, cancellationToken: cancellationToken);

                    var outputResponse = new TrakHoundServiceResponse<string>();
                    outputResponse.StatusCode = response.StatusCode;
                    outputResponse.Message = response.Message;

                    if (response.Result != null && !response.Result.Values.IsNullOrEmpty())
                    {
                        outputResponse.Result = response.Result.Values.FirstOrDefault().Value;
                    }

                    return outputResponse;
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection) && !keys.IsNullOrEmpty())
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.KeyValues.Read(collection, keys, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse<int>> Count(string collection, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(collection))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var response = await router.KeyValues.Count(collection, cancellationToken: cancellationToken);

                    var outputResponse = new TrakHoundServiceResponse<int>();
                    outputResponse.StatusCode = response.StatusCode;
                    outputResponse.Message = response.Message;
                    outputResponse.Result = response.Result;

                    return outputResponse;
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse> Publish(string collection, string key, string value, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    var entries = new Dictionary<string, string>();
                    entries.Add(key, value);

                    return await router.KeyValues.Publish(collection, entries, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!entries.IsNullOrEmpty())
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.KeyValues.Publish(collection, entries, cancellationToken: cancellationToken);
                }
            }

            return default;
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!keys.IsNullOrEmpty())
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.KeyValues.Delete(collection, keys, cancellationToken: cancellationToken);
                }
            }

            return default;
        }
    }
}
