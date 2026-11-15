// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundTimeSeriesServiceClient that uses the Time Series Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpTimeSeriesServiceClient : TrakHoundHttpClientBase, ITrakHoundTimeSeriesServiceClient
    {
        /// <summary>
        /// Creates a new Time Series Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpTimeSeriesServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "availability");
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
        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "list/measurements");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return GetResponse<IEnumerable<string>>(await Requests.GetResponse(url, cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "list/series");
            url = Url.AddQueryParameter(url, "measurement", measurement);
            url = Url.AddQueryParameter(url, "skip", skip);
            url = Url.AddQueryParameter(url, "take", take);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return GetResponse<IEnumerable<TrakHoundTimeSeries>>(await Requests.PostResponse(url, (object)(tags ?? new Dictionary<string, string>()), cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "list/fields");
            url = Url.AddQueryParameter(url, "measurement", measurement);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return GetResponse<IEnumerable<TrakHoundTimeSeriesField>>(await Requests.GetResponse(url, cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "write");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, (object)points?.ToArray(), cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = GetMessage(httpResponse);
            return serviceResponse;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "read");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return GetResponse<TrakHoundTimeSeriesResult[]>(await Requests.PostResponse(url, (object)query, cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "read/latest");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var query = new TrakHoundTimeSeriesQuery { Measurement = measurement, Tags = tags?.ToDictionary(o => o.Key, o => o.Value), Fields = fields?.ToArray() };

            return GetResponse<TrakHoundTimeSeriesLatestResult[]>(await Requests.PostResponse(url, (object)query, cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix, "aggregate");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return GetResponse<TrakHoundTimeSeriesResult[]>(await Requests.PostResponse(url, (object)query, cancellationToken: cancellationToken));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.TimeSeriesServicePrefix);
            url = Url.AddQueryParameter(url, "measurement", measurement);
            url = Url.AddQueryParameter(url, "start", start.UtcDateTime.ToString("o"));
            url = Url.AddQueryParameter(url, "stop", stop.UtcDateTime.ToString("o"));
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.DeleteResponse(url, (object)(tags ?? new Dictionary<string, string>()), cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = GetMessage(httpResponse);
            return serviceResponse;
        }


        private static TrakHoundServiceResponse<TResult> GetResponse<TResult>(TrakHoundHttpResponse httpResponse)
        {
            var serviceResponse = new TrakHoundServiceResponse<TResult>();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = GetMessage(httpResponse);
            if (httpResponse.StatusCode >= 200 && httpResponse.StatusCode < 300) serviceResponse.Result = httpResponse.GetJsonContentObject<TResult>();
            return serviceResponse;
        }

        // The Instance sends the Service's message as the content, which says why (ex. which Field has another type)
        private static string GetMessage(TrakHoundHttpResponse httpResponse)
        {
            if (httpResponse.StatusCode >= 200 && httpResponse.StatusCode < 300) return httpResponse.Message;

            var content = httpResponse.GetContentUtf8String();
            return !string.IsNullOrEmpty(content) ? content : httpResponse.Message;
        }
    }
}
