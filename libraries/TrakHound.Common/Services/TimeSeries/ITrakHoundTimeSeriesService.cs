// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// A Service that stores timestamped Values by Measurement and Tags. An operation the Service doesn't support returns 405, so a Router tries the next Target
    /// </summary>
    public interface ITrakHoundTimeSeriesService : ITrakHoundService
    {
        /// <summary>
        /// Lists the Measurements in the Service
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Series (unique Tag combinations) of a Measurement that have all of the specified Tags, paged using skip and take
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Fields of a Measurement and their types, in name order. A Point often has only some of them
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes Points. A Point with the same Measurement, Tags and Timestamp as an existing Point replaces its Quality and only the Fields it supplies.
        /// A failed batch may be partly applied, so retry the whole batch
        /// </summary>
        Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Points between Start (inclusive) and Stop (exclusive), with a Result for each Series
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the newest Value of each Field (null reads every Field) of each Series that has one of the Values of each of the specified Tags
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculates the Functions for each Interval between Start (inclusive) and Stop (exclusive), with a Result for each Series
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the Points between start (inclusive) and stop (exclusive) of each Series that has all of the specified Tags
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default);
    }
}
