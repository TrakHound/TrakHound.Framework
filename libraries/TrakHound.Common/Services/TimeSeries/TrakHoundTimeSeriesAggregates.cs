// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// Builds Aggregate Results the same way for every Time Series Service, so Intervals and Fill don't differ between databases
    /// </summary>
    public static class TrakHoundTimeSeriesAggregates
    {
        /// <summary>
        /// Gets the start of the Interval (aligned to the Unix epoch) that contains the UTC ticks
        /// </summary>
        public static long GetIntervalStart(long ticks, long interval)
        {
            var offset = (ticks - DateTimeOffset.UnixEpoch.UtcTicks) % interval;
            if (offset < 0) offset += interval;
            return ticks - offset;
        }

        /// <summary>
        /// Returns whether a Function weights Values by how long they held
        /// </summary>
        public static bool IsTimeWeighted(TrakHoundTimeSeriesAggregate function) => function == TrakHoundTimeSeriesAggregate.TimeWeightedMean || function == TrakHoundTimeSeriesAggregate.DurationInState;

        /// <summary>
        /// Calculates TimeWeightedMean or DurationInState for each Interval (Interval start in UTC ticks => Value). The Points have the Field and are in time order,
        /// and include the last one before Start. A Value holds from its Point until the next Point, but never past Stop or the current time.
        /// A skipped Bad Point or a Point without a Value ends the Value before it, and the time until the next Point isn't counted
        /// </summary>
        public static Dictionary<long, object> CalculateTimeWeighted(TrakHoundTimeSeriesAggregateQuery query, TrakHoundTimeSeriesAggregate function, IReadOnlyList<(long Ticks, object Value, TrakHoundTimeSeriesQuality Quality)> points, object state)
        {
            var interval = query.Interval.Ticks;
            var end = Math.Min(query.Stop.UtcTicks, DateTimeOffset.UtcNow.UtcTicks);

            var weights = new Dictionary<long, (double Weighted, double Seconds)>();
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].Value == null) continue;
                if (!query.IncludeBadQuality && points[i].Quality == TrakHoundTimeSeriesQuality.Bad) continue;

                var from = Math.Max(points[i].Ticks, query.Start.UtcTicks);
                var to = Math.Min(i + 1 < points.Count ? points[i + 1].Ticks : end, end);
                var value = points[i].Value;

                while (from < to)
                {
                    var bucket = GetIntervalStart(from, interval);
                    var next = Math.Min(bucket + interval, to);
                    var seconds = TimeSpan.FromTicks(next - from).TotalSeconds;

                    weights.TryGetValue(bucket, out var weight);
                    var weighted = function == TrakHoundTimeSeriesAggregate.DurationInState
                        ? (IsState(value, state) ? seconds : 0)
                        : Convert.ToDouble(value) * seconds;
                    weights[bucket] = (weight.Weighted + weighted, weight.Seconds + seconds);

                    from = next;
                }
            }

            return weights.ToDictionary(o => o.Key, o => (object)(function == TrakHoundTimeSeriesAggregate.DurationInState ? o.Value.Weighted : o.Value.Weighted / o.Value.Seconds));
        }

        // A whole number can be the State of a double Field
        private static bool IsState(object value, object state)
        {
            return value is double number && state is long whole ? number == whole : value.Equals(state);
        }

        /// <summary>
        /// Creates the Result of a Series from its calculated values (Interval start in UTC ticks => Column => Value). Returns null if there aren't any
        /// </summary>
        public static TrakHoundTimeSeriesResult CreateResult(TrakHoundTimeSeriesAggregateQuery query, Dictionary<string, string> tags, Dictionary<long, Dictionary<string, object>> values)
        {
            // A Series with none of the Fields in the range has nothing to fill
            if (values == null || values.Count < 1) return null;

            var columns = new List<string> { TrakHoundTimeSeriesResult.TimestampColumn };
            var counts = new HashSet<string>();
            foreach (var field in query.Fields)
            {
                foreach (var function in query.Functions)
                {
                    columns.Add(TrakHoundTimeSeriesAggregateQuery.GetColumn(field, function));
                    if (function == TrakHoundTimeSeriesAggregate.Count) counts.Add(columns.Last());
                }
            }

            var rows = new List<object[]>();
            var previous = new Dictionary<string, object>();
            var interval = query.Interval.Ticks;
            for (var start = GetIntervalStart(query.Start.UtcTicks, interval); start < query.Stop.UtcTicks; start += interval)
            {
                values.TryGetValue(start, out var row);
                if (row == null && query.Fill == TrakHoundTimeSeriesFill.None) continue;

                var output = new object[columns.Count];
                output[0] = new DateTimeOffset(start, TimeSpan.Zero);
                for (var i = 1; i < columns.Count; i++)
                {
                    object value = null;
                    if (row == null || !row.TryGetValue(columns[i], out value))
                    {
                        if (counts.Contains(columns[i])) value = 0L;
                        else if (query.Fill == TrakHoundTimeSeriesFill.Previous) previous.TryGetValue(columns[i], out value);
                    }

                    output[i] = value;
                    if (value != null) previous[columns[i]] = value;
                }

                rows.Add(output);
            }

            return new TrakHoundTimeSeriesResult { Measurement = query.Measurement, Tags = new Dictionary<string, string>(tags), Columns = columns.ToArray(), Rows = rows.ToArray() };
        }
    }
}
