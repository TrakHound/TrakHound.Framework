// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Http.Service
{
    public class HttpTimeSeriesService : HttpService, ITrakHoundTimeSeriesService
    {
        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.TimeSeries;


        public HttpTimeSeriesService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<string>>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.ListMeasurements(cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundTimeSeries>>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.ListSeries(measurement, tags, skip, take, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<IEnumerable<TrakHoundTimeSeriesField>>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.ListFields(measurement, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.Write(points, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTimeSeriesResult[]>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.Read(query, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTimeSeriesLatestResult[]>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.ReadLatest(measurement, tags, fields, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTimeSeriesResult[]>(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.Aggregate(query, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.TimeSeries.Delete(measurement, tags, start, stop, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
