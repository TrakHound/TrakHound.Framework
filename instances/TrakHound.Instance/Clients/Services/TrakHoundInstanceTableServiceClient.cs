// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceTableServiceClient : ITrakHoundTableServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceTableServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Tables.CheckAvailability(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Tables.List(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundTable[]>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Tables.GetColumns(tableName, cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundTableColumn[]>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<string[]>> GetDialects(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Tables.GetDialects(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<string[]>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (request != null && !request.Steps.IsNullOrEmpty())
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Tables.Execute(request, cancellationToken: cancellationToken);
                }
                else
                {
                    return TrakHoundServiceResponse<TrakHoundTableStepResult[]>.BadRequest($"Invalid Router ID : {routerId}");
                }
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundTableStepResult[]>.BadRequest("No Steps Specified");
            }
        }

        public async Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(query))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Tables.ExecuteNonQuery(query, timeout, parameters, cancellationToken: cancellationToken);
                }
                else
                {
                    return TrakHoundServiceResponse.BadRequest($"Invalid Router ID : {routerId}");
                }
            }
            else
            {
                return TrakHoundServiceResponse.BadRequest("Query Not Specified");
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(query))
            {
                var router = _baseClient.GetRouter(routerId);
                if (router != null)
                {
                    return await router.Tables.ExecuteQuery(query, timeout, parameters, cancellationToken: cancellationToken);
                }
                else
                {
                    return TrakHoundServiceResponse<TrakHoundTableResult[]>.BadRequest($"Invalid Router ID : {routerId}");
                }
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundTableResult[]>.BadRequest("Query Not Specified");
            }
        }

        public async Task<TrakHoundServiceResponse<string>> ReadSingleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadSingleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadMultipleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<T>> ReadSingle<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadSingle<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadMultiple<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }
    }
}
