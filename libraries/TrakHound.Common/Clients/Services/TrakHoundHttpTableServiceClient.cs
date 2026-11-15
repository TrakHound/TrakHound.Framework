// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundTableServiceClient that uses the Table Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpTableServiceClient : TrakHoundHttpClientBase, ITrakHoundTableServiceClient
    {
        /// <summary>
        /// Creates a new Table Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpTableServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "availability");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundServiceAvailabilityResponse>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "list");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundTable[]>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundTable[]>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "columns");
            url = Url.AddQueryParameter(url, "tableName", tableName);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundTableColumn[]>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundTableColumn[]>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<string[]>> GetDialects(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "dialects");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<string[]>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<string[]>();
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "execute");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, (object)request, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundTableStepResult[]>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;

            if (httpResponse.Success)
            {
                serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundTableStepResult[]>();
            }
            else
            {
                serviceResponse.Message = GetErrorMessage(httpResponse);
            }

            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? commandTimeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix, "query");
            if (commandTimeout.HasValue) url = Url.AddQueryParameter(url, "timeout", commandTimeout.Value);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await PostQuery(url, query, parameters, cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundTableResult[]>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;

            if (httpResponse.Success)
            {
                serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundTableResult[]>();
            }
            else
            {
                serviceResponse.Message = GetErrorMessage(httpResponse);
            }

            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? commandTimeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TableServicePrefix);
            if (commandTimeout.HasValue) url = Url.AddQueryParameter(url, "timeout", commandTimeout.Value);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await PostQuery(url, query, parameters, cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;

            // The body is the Service's message (ex. "(1 rows affected)" or the SQL error)
            serviceResponse.Message = httpResponse.Success ? httpResponse.GetContentUtf8String() : GetErrorMessage(httpResponse);

            return serviceResponse;
        }

        // The error text the Service returned (ex. the SQL error), or the HTTP reason phrase if there is none
        private static string GetErrorMessage(TrakHoundHttpResponse httpResponse)
        {
            var content = httpResponse.GetContentUtf8String();
            return !string.IsNullOrEmpty(content) ? content : httpResponse.Message;
        }

        // Send the raw Query as text unless Parameters are specified (JSON body)
        private Task<TrakHoundHttpResponse> PostQuery(string url, string query, TrakHoundTableParameterCollection parameters, CancellationToken cancellationToken)
        {
            if (parameters != null && parameters.Count > 0)
            {
                return Requests.PostResponse(url, (object)new TrakHoundTableQueryRequest(query, parameters), cancellationToken: cancellationToken);
            }
            else
            {
                return Requests.PostResponse(url, query, cancellationToken: cancellationToken);
            }
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<string>> ReadSingleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadSingleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadMultipleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<T>> ReadSingle<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadSingle<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return await TrakHoundTableServiceClient.ReadMultiple<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
        }
    }
}
