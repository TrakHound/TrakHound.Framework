// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Clients.Services
{
    public class TrakHoundInstanceTimeSeriesServiceClient : ITrakHoundTimeSeriesServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceTimeSeriesServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.CheckAvailability(cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.ListMeasurements(cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<IEnumerable<string>>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.ListSeries(measurement, tags, skip, take, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.ListFields(measurement, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.Write(points, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.Read(query, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.ReadLatest(measurement, tags, fields, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.Aggregate(query, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>.BadRequest($"Invalid Router ID : {routerId}");
        }

        public async Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null) return await router.TimeSeries.Delete(measurement, tags, start, stop, cancellationToken: cancellationToken);

            return TrakHoundServiceResponse.BadRequest($"Invalid Router ID : {routerId}");
        }
    }
}
