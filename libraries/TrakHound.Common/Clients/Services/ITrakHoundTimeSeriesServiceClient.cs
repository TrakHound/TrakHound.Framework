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
    /// <summary>
    /// Client used to write, read, aggregate, and delete Points in the Time Series Service
    /// </summary>
    public interface ITrakHoundTimeSeriesServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Lists the Measurements available through the Router
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Series of a Measurement that have all of the specified Tags (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Fields of a Measurement and their types
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes Points. A failed batch may be partly applied, so retry the whole batch
        /// </summary>
        Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Points between Start (inclusive) and Stop (exclusive), with a Result for each Series
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the newest Value of each Field of each Series
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculates the Functions for each Interval between Start (inclusive) and Stop (exclusive), with a Result for each Series
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the Points between start (inclusive) and stop (exclusive) of each Series that has all of the specified Tags
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, string routerId = null, CancellationToken cancellationToken = default);
    }
}
