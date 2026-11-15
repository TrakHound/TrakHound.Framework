// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Testing.Services
{
    /// <summary>
    /// A Time Series Service that keeps its Points in memory (the default for Client.Services.TimeSeries in TrakHoundTestClient)
    /// </summary>
    public class TrakHoundMemoryTimeSeriesService : TrakHoundService, ITrakHoundTimeSeriesService
    {
        private class StoredPoint
        {
            // A Field without a Value has a null Value
            public readonly Dictionary<string, (object Value, TrakHoundTimeSeriesQuality Quality)> Fields = new();
        }

        private class StoredSeries
        {
            public Dictionary<string, string> Tags;
            public readonly SortedList<DateTimeOffset, StoredPoint> Points = new();
        }

        private class StoredMeasurement
        {
            public readonly Dictionary<string, TrakHoundTimeSeriesFieldType?> FieldTypes = new();
            public readonly SortedDictionary<string, StoredSeries> Series = new(StringComparer.Ordinal);
        }


        private readonly SortedDictionary<string, StoredMeasurement> _measurements = new(StringComparer.Ordinal);
        private readonly object _lock = new();


        public TrakHoundMemoryTimeSeriesService() : base(new TrakHoundServiceConfiguration { Id = "memory-timeseries" }) { }


        public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ok(new TrakHoundServiceAvailabilityResponse { IsAvailable = true, Message = "Available" }));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(Ok<IEnumerable<string>>(_measurements.Keys.ToList()));
            }
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeries>>(message));

            lock (_lock)
            {
                var series = GetSeries(measurement, TrakHoundTimeSeriesQuery.GetTagFilter(tags)).Skip(Math.Max(skip, 0)).Take(Math.Max(take, 0)).Select(o => new TrakHoundTimeSeries { Measurement = measurement, Tags = new(o.Tags) });
                return Task.FromResult(Ok<IEnumerable<TrakHoundTimeSeries>>(series.ToList()));
            }
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeriesField>>(message));

            lock (_lock)
            {
                IEnumerable<TrakHoundTimeSeriesField> fields = _measurements.TryGetValue(measurement, out var stored)
                    ? stored.FieldTypes.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new TrakHoundTimeSeriesField { Name = o.Key, Type = o.Value }).ToList()
                    : new List<TrakHoundTimeSeriesField>();

                return Task.FromResult(Ok(fields));
            }
        }

        public Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken = default)
        {
            if (points == null) return Task.FromResult(BadRequest("Points are required"));

            var items = points.ToList();
            foreach (var point in items)
            {
                var message = TrakHoundTimeSeriesValidation.ValidatePoint(point);
                if (message != null) return Task.FromResult(BadRequest(message));
            }

            lock (_lock)
            {
                // Check every type first, so a rejected batch changes nothing
                var typeMessage = TrakHoundTimeSeriesValidation.GetWriteTypes(items, (measurement, field) => _measurements.TryGetValue(measurement, out var stored) && stored.FieldTypes.TryGetValue(field, out var type) ? type : null, out var types);
                if (typeMessage != null) return Task.FromResult(BadRequest(typeMessage));

                foreach (var point in items)
                {
                    if (!_measurements.TryGetValue(point.Measurement, out var measurement))
                    {
                        measurement = new StoredMeasurement();
                        _measurements[point.Measurement] = measurement;
                    }

                    var key = GetSeriesKey(point.Tags);
                    if (!measurement.Series.TryGetValue(key, out var series))
                    {
                        series = new StoredSeries { Tags = point.Tags != null ? new(point.Tags) : new() };
                        measurement.Series[key] = series;
                    }

                    var timestamp = point.Timestamp.ToUniversalTime();
                    if (!series.Points.TryGetValue(timestamp, out var stored))
                    {
                        stored = new StoredPoint();
                        series.Points[timestamp] = stored;
                    }

                    foreach (var field in point.Fields)
                    {
                        var quality = point.GetQuality(field.Key);

                        if (TrakHoundTimeSeriesValidation.IsNoValue(field.Value))
                        {
                            stored.Fields[field.Key] = (null, quality);
                            if (!measurement.FieldTypes.ContainsKey(field.Key)) measurement.FieldTypes[field.Key] = null;
                            continue;
                        }

                        var type = types[(point.Measurement, field.Key)];
                        stored.Fields[field.Key] = (TrakHoundTimeSeriesValidation.ConvertFieldValue(TrakHoundTimeSeriesValidation.GetFieldValue(field.Value, out _), type), quality);
                        measurement.FieldTypes[field.Key] = type;
                    }
                }
            }

            return Task.FromResult(Ok());
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            var take = TrakHoundTimeSeriesValidation.GetTake(query.Take);
            var results = new List<TrakHoundTimeSeriesResult>();

            lock (_lock)
            {
                foreach (var series in GetSeries(query.Measurement, query.Tags))
                {
                    var points = series.Points.Where(o => HasField(o.Value, query.Fields)).ToList();
                    var inRange = points.Where(o => o.Key >= query.Start && o.Key < query.Stop);
                    var selected = (query.Descending ? inRange.Reverse() : inRange).Take(take + 1).ToList();

                    var hasMore = selected.Count > take;
                    if (hasMore) selected.RemoveAt(take);

                    if (query.IncludePrevious)
                    {
                        var previous = points.LastOrDefault(o => o.Key < query.Start);
                        if (previous.Value != null)
                        {
                            if (query.Descending) selected.Add(previous);
                            else selected.Insert(0, previous);
                        }
                    }

                    if (selected.Count > 0) results.Add(CreateResult(query.Measurement, series, query.Fields, selected, hasMore));
                }
            }

            return Task.FromResult(Ok(results.ToArray()));
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default)
        {
            var fieldNames = fields?.ToArray();
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTagFilter(tags) ?? TrakHoundTimeSeriesValidation.ValidateFields(fieldNames);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesLatestResult[]>(message));

            var results = new List<TrakHoundTimeSeriesLatestResult>();

            lock (_lock)
            {
                foreach (var series in GetSeries(measurement, tags))
                {
                    var names = fieldNames ?? series.Points.Values.SelectMany(o => o.Fields.Keys).Distinct().OrderBy(o => o, StringComparer.Ordinal).ToArray();

                    var values = new List<TrakHoundTimeSeriesLatestValue>();
                    foreach (var name in names)
                    {
                        var latest = series.Points.LastOrDefault(o => o.Value.Fields.ContainsKey(name));
                        if (latest.Value == null) continue;

                        var field = latest.Value.Fields[name];
                        values.Add(new TrakHoundTimeSeriesLatestValue { Field = name, Timestamp = latest.Key, Value = field.Value, Quality = field.Quality });
                    }

                    if (values.Count > 0) results.Add(new TrakHoundTimeSeriesLatestResult { Measurement = measurement, Tags = new(series.Tags), Values = values.ToArray() });
                }
            }

            return Task.FromResult(Ok(results.ToArray()));
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            var results = new List<TrakHoundTimeSeriesResult>();

            lock (_lock)
            {
                if (!_measurements.TryGetValue(query.Measurement, out var measurement)) return Task.FromResult(Ok(results.ToArray()));

                message = TrakHoundTimeSeriesValidation.ValidateTypes(query, field => measurement.FieldTypes.TryGetValue(field, out var type) ? type : null);
                if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

                foreach (var series in GetSeries(query.Measurement, query.Tags))
                {
                    var result = CreateAggregateResult(query, series);
                    if (result != null) results.Add(result);
                }
            }

            return Task.FromResult(Ok(results.ToArray()));
        }

        public Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags) ?? TrakHoundTimeSeriesValidation.ValidateRange(start, stop);
            if (message != null) return Task.FromResult(BadRequest(message));

            lock (_lock)
            {
                foreach (var series in GetSeries(measurement, TrakHoundTimeSeriesQuery.GetTagFilter(tags)).ToList())
                {
                    foreach (var timestamp in series.Points.Keys.Where(o => o >= start && o < stop).ToList()) series.Points.Remove(timestamp);
                }

                if (_measurements.TryGetValue(measurement, out var stored))
                {
                    foreach (var empty in stored.Series.Where(o => o.Value.Points.Count < 1).Select(o => o.Key).ToList()) stored.Series.Remove(empty);
                    if (stored.Series.Count < 1) _measurements.Remove(measurement);
                }
            }

            return Task.FromResult(Ok());
        }


        private static TrakHoundTimeSeriesResult CreateAggregateResult(TrakHoundTimeSeriesAggregateQuery query, StoredSeries series)
        {
            // Interval start => Column => Value
            var values = new Dictionary<long, Dictionary<string, object>>();
            var state = TrakHoundTimeSeriesValidation.GetFieldValue(query.State, out _);

            foreach (var field in query.Fields)
            {
                foreach (var function in query.Functions)
                {
                    var column = TrakHoundTimeSeriesAggregateQuery.GetColumn(field, function);
                    var calculated = TrakHoundTimeSeriesAggregates.IsTimeWeighted(function)
                        ? TrakHoundTimeSeriesAggregates.CalculateTimeWeighted(query, function, series.Points.Where(o => o.Value.Fields.ContainsKey(field)).Select(o => (o.Key.UtcTicks, o.Value.Fields[field].Value, o.Value.Fields[field].Quality)).ToList(), state)
                        : Calculate(query, series, field, function);

                    foreach (var value in calculated)
                    {
                        if (!values.TryGetValue(value.Key, out var row))
                        {
                            row = new Dictionary<string, object>();
                            values[value.Key] = row;
                        }

                        row[column] = value.Value;
                    }
                }
            }

            return TrakHoundTimeSeriesAggregates.CreateResult(query, series.Tags, values);
        }

        private static Dictionary<long, object> Calculate(TrakHoundTimeSeriesAggregateQuery query, StoredSeries series, string field, TrakHoundTimeSeriesAggregate function)
        {
            var groups = series.Points
                .Where(o => o.Key >= query.Start && o.Key < query.Stop && o.Value.Fields.TryGetValue(field, out var stored) && stored.Value != null)
                .Where(o => query.IncludeBadQuality || o.Value.Fields[field].Quality != TrakHoundTimeSeriesQuality.Bad)
                .GroupBy(o => TrakHoundTimeSeriesAggregates.GetIntervalStart(o.Key.UtcTicks, query.Interval.Ticks));

            var output = new Dictionary<long, object>();
            foreach (var group in groups)
            {
                var values = group.Select(o => o.Value.Fields[field].Value).ToList();
                output[group.Key] = function switch
                {
                    TrakHoundTimeSeriesAggregate.Mean => values.Average(o => Convert.ToDouble(o)),
                    TrakHoundTimeSeriesAggregate.Min => values.Min(),
                    TrakHoundTimeSeriesAggregate.Max => values.Max(),
                    TrakHoundTimeSeriesAggregate.Sum => values[0] is long ? values.Sum(o => Convert.ToInt64(o)) : (object)values.Sum(o => Convert.ToDouble(o)),
                    TrakHoundTimeSeriesAggregate.Count => (long)values.Count,
                    TrakHoundTimeSeriesAggregate.First => values[0],
                    _ => values[^1]
                };
            }

            return output;
        }

        private IEnumerable<StoredSeries> GetSeries(string measurement, IReadOnlyDictionary<string, string[]> tags)
        {
            if (!_measurements.TryGetValue(measurement, out var stored)) return Enumerable.Empty<StoredSeries>();
            return stored.Series.Values.Where(o => tags == null || tags.All(tag => o.Tags.TryGetValue(tag.Key, out var value) && tag.Value.Contains(value)));
        }

        private static TrakHoundTimeSeriesResult CreateResult(string measurement, StoredSeries series, string[] fields, IEnumerable<KeyValuePair<DateTimeOffset, StoredPoint>> points, bool hasMore)
        {
            var selected = points.ToList();
            var names = fields ?? selected.SelectMany(o => o.Value.Fields.Keys).Distinct().OrderBy(o => o, StringComparer.Ordinal).ToArray();
            var columns = TrakHoundTimeSeriesResult.GetColumns(names);

            var rows = selected.Select(point =>
            {
                var row = new object[columns.Length];
                row[0] = point.Key;
                for (var i = 0; i < names.Length; i++)
                {
                    if (!point.Value.Fields.TryGetValue(names[i], out var field)) continue;

                    row[i * 2 + 1] = field.Value;
                    row[i * 2 + 2] = field.Quality.ToString();
                }

                return row;
            });

            return new TrakHoundTimeSeriesResult { Measurement = measurement, Tags = new(series.Tags), Columns = columns, Rows = rows.ToArray(), HasMore = hasMore };
        }

        private static bool HasField(StoredPoint point, string[] fields) => fields == null || fields.Any(point.Fields.ContainsKey);

        private static string GetSeriesKey(IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null) return string.Empty;
            return string.Join("\n", tags.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));
        }
    }
}
