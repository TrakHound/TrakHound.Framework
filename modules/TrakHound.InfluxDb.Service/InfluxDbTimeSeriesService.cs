// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.InfluxDb.Service
{
    /// <summary>
    /// A Time Series Service that stores Points in InfluxDB 3, using its HTTP API. Writes use line protocol and everything else uses SQL.
    /// Each Field's Quality is another Field ("$q_" and the Field's name), since a Tag would make a Point with a new Quality a different Series. A Point can't clear a Field,
    /// so a Field written without a Value stores _qualityNoValue, which also hides a Value written at that time before
    /// </summary>
    public class InfluxDbTimeSeriesService : TrakHoundService, ITrakHoundTimeSeriesService
    {
        private const string _timeColumn = "time";
        private const string _qualityPrefix = "$q_";
        private const int _qualityNoValue = 3;
        private const string _origin = "timestamp '1970-01-01T00:00:00Z'";
        private const string _defaultDatabase = "trakhound";
        private const string _tagType = "Dictionary";
        private const int _maxBodySize = 8 * 1024 * 1024; // InfluxDB rejects bodies over 10 MB by default
        private const int _defaultTimeout = 30000;
        private const int _defaultMaxTimeWeightedPoints = 1000000;
        private static readonly TimeSpan _availabilityCacheTime = TimeSpan.FromSeconds(60);

        private readonly HttpClient _client;
        private readonly string _database;
        private readonly int _maxTimeWeightedPoints;
        private TrakHoundServiceAvailabilityResponse _availability;
        private DateTime _availabilityTime;


        private class Schema
        {
            public readonly List<string> Tags = new();

            // Null until a Field has a Value, since only its Quality Column exists until then
            public readonly Dictionary<string, TrakHoundTimeSeriesFieldType?> Fields = new();

            // Points written by something else have no Quality Column
            public readonly HashSet<string> Qualities = new();
        }

        /// <summary>
        /// A failed request to the server, with the Status Code a Service Router should see
        /// </summary>
        private class InfluxException : Exception
        {
            public int StatusCode { get; }

            public InfluxException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }
        }


        public InfluxDbTimeSeriesService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            var url = configuration?.GetParameter("url");
            var database = configuration?.GetParameter("database");
            var token = configuration?.GetParameter("token");
            var timeout = configuration != null && configuration.ParameterExists("timeout") ? configuration.GetParameter<int>("timeout") : _defaultTimeout;

            _database = !string.IsNullOrEmpty(database) ? database : _defaultDatabase;

            var maxPoints = configuration != null && configuration.ParameterExists("maxTimeWeightedPoints") ? configuration.GetParameter<int>("maxTimeWeightedPoints") : 0;
            _maxTimeWeightedPoints = maxPoints > 0 ? maxPoints : _defaultMaxTimeWeightedPoints;

            _client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeout)) };
            if (!string.IsNullOrEmpty(url)) _client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            if (!string.IsNullOrEmpty(token)) _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var cached = _availability;
            if (cached != null && DateTime.UtcNow - _availabilityTime < _availabilityCacheTime) return Ok(cached);

            var response = new TrakHoundServiceAvailabilityResponse();
            try
            {
                using var health = await _client.GetAsync("health", cancellationToken);
                response.IsAvailable = health.IsSuccessStatusCode;
                response.Message = health.IsSuccessStatusCode ? "Available" : $"The server returned {(int)health.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                response.IsAvailable = false;
                response.Message = ex.Message;
            }

            _availability = response;
            _availabilityTime = DateTime.UtcNow;
            return Ok(response);
        }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(CancellationToken cancellationToken = default)
        {
            return Run<IEnumerable<string>>(async () =>
            {
                var rows = await Query("select table_name from information_schema.tables where table_schema = 'iox' order by table_name", null, cancellationToken);
                return Ok<IEnumerable<string>>(rows.Select(o => o["table_name"].GetString()).ToList());
            });
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeries>>(message));

            return Run<IEnumerable<TrakHoundTimeSeries>>(async () =>
            {
                var schema = await GetSchema(measurement, cancellationToken);
                if (schema == null) return Ok<IEnumerable<TrakHoundTimeSeries>>(new List<TrakHoundTimeSeries>());

                var series = await GetSeries(measurement, schema, TrakHoundTimeSeriesQuery.GetTagFilter(tags), cancellationToken);
                var output = series.Skip(Math.Max(skip, 0)).Take(Math.Max(take, 0)).Select(o => new TrakHoundTimeSeries { Measurement = measurement, Tags = o }).ToList();
                return Ok<IEnumerable<TrakHoundTimeSeries>>(output);
            });
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeriesField>>(message));

            return Run<IEnumerable<TrakHoundTimeSeriesField>>(async () =>
            {
                var schema = await GetSchema(measurement, cancellationToken);
                var fields = schema?.Fields.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new TrakHoundTimeSeriesField { Name = o.Key, Type = o.Value }).ToList();
                return Ok<IEnumerable<TrakHoundTimeSeriesField>>(fields ?? new List<TrakHoundTimeSeriesField>());
            });
        }

        public async Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken = default)
        {
            if (points == null) return BadRequest("Points are required");

            var items = points.ToList();
            foreach (var point in items)
            {
                var message = TrakHoundTimeSeriesValidation.ValidatePoint(point);
                if (message != null) return BadRequest(message);

                // Line protocol has no way to write these
                if (point.Tags != null && point.Tags.Values.Any(o => o.Contains('\n') || o.EndsWith('\\'))) return BadRequest("A Tag Value can't contain a line break or end with a backslash");
            }

            var response = await Run<bool>(async () =>
            {
                // A whole number can be written to a double Field, but line protocol would send it as a long, so the Service needs the Field's type.
                // Only a batch with whole numbers has to look it up
                var stored = await GetStoredTypes(items, false, cancellationToken);
                var typeMessage = TrakHoundTimeSeriesValidation.GetWriteTypes(items, (measurement, field) => stored.TryGetValue((measurement, field), out var type) ? type : null, out var types);
                if (typeMessage != null) return BadRequest<bool>(typeMessage);

                try
                {
                    var body = new StringBuilder();
                    foreach (var point in items)
                    {
                        AppendLine(body, point, types);
                        if (body.Length >= _maxBodySize)
                        {
                            await SendLines(body.ToString(), cancellationToken);
                            body.Clear();
                        }
                    }

                    if (body.Length > 0) await SendLines(body.ToString(), cancellationToken);
                }
                catch (InfluxException) when (types.Count > 0)
                {
                    // The server's message names its own types, so say which Field is wrong in the same words as the other Services. Only a failed write pays for this lookup
                    var all = await GetStoredTypes(items, true, cancellationToken);
                    var conflict = TrakHoundTimeSeriesValidation.GetWriteTypes(items, (measurement, field) => all.TryGetValue((measurement, field), out var type) ? type : null, out _);
                    if (conflict != null) return BadRequest<bool>(conflict);

                    throw;
                }

                return Ok(true);
            });

            return new TrakHoundServiceResponse(response.StatusCode, response.Message);
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            return Run(async () =>
            {
                var schema = await GetSchema(query.Measurement, cancellationToken);
                if (schema == null) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                var names = query.Fields ?? schema.Fields.Keys.OrderBy(o => o, StringComparer.Ordinal).ToArray();
                var existing = names.Where(schema.Fields.ContainsKey).ToArray();
                if (existing.Length < 1) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                var hasField = "(" + string.Join(" or ", existing.Select(o => GetHasField(schema, o))) + ")";
                var take = TrakHoundTimeSeriesValidation.GetTake(query.Take);
                var direction = query.Descending ? "desc" : "asc";

                var parameters = new Dictionary<string, string>();
                var filter = GetTagsFilter(query.Tags, parameters);
                if (!HasTags(schema, query.Tags)) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                // One more than Take, which says whether Take left out Points
                var found = await QueryTop(query.Measurement, schema, existing, $"{filter} and {hasField} and time >= {Literal(query.Start)} and time < {Literal(query.Stop)}", direction, take + 1, parameters, cancellationToken);

                var rows = new List<Dictionary<string, JsonElement>>();
                var counts = new Dictionary<string, int>();
                var hasMore = new HashSet<string>();
                foreach (var row in found)
                {
                    var key = GetSeriesKey(GetTags(row, schema));
                    counts[key] = counts.GetValueOrDefault(key) + 1;

                    if (counts[key] > take) hasMore.Add(key);
                    else rows.Add(row);
                }

                if (query.IncludePrevious)
                {
                    var previous = await QueryTop(query.Measurement, schema, existing, $"{filter} and {hasField} and time < {Literal(query.Start)}", "desc", 1, parameters, cancellationToken);
                    if (query.Descending) rows.AddRange(previous);
                    else rows.InsertRange(0, previous);
                }

                var results = GroupBySeries(rows, schema).Select(o => CreateResult(query.Measurement, o.Tags, schema, names, query.Fields == null, o.Rows, hasMore.Contains(GetSeriesKey(o.Tags)))).ToArray();
                return Ok(results);
            });
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default)
        {
            var fieldNames = fields?.ToArray();
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTagFilter(tags) ?? TrakHoundTimeSeriesValidation.ValidateFields(fieldNames);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesLatestResult[]>(message));

            return Run(async () =>
            {
                var schema = await GetSchema(measurement, cancellationToken);
                if (schema == null) return Ok(Array.Empty<TrakHoundTimeSeriesLatestResult>());

                var names = fieldNames ?? schema.Fields.Keys.OrderBy(o => o, StringComparer.Ordinal).ToArray();

                var parameters = new Dictionary<string, string>();
                var filter = GetTagsFilter(tags, parameters);
                if (!HasTags(schema, tags)) return Ok(Array.Empty<TrakHoundTimeSeriesLatestResult>());

                var results = new SortedDictionary<string, TrakHoundTimeSeriesLatestResult>(StringComparer.Ordinal);
                var values = new Dictionary<string, List<TrakHoundTimeSeriesLatestValue>>();

                // One Query for each Field, since each Field's newest row can be a different one
                foreach (var name in names.Where(schema.Fields.ContainsKey).Distinct())
                {
                    var rows = await QueryTop(measurement, schema, new[] { name }, $"{filter} and {GetHasField(schema, name)}", "desc", 1, parameters, cancellationToken);

                    foreach (var group in GroupBySeries(rows, schema))
                    {
                        var row = group.Rows[0];
                        if (!ReadField(row, schema, name, out var value, out var quality)) continue;

                        var key = GetSeriesKey(group.Tags);
                        if (!values.TryGetValue(key, out var seriesValues))
                        {
                            seriesValues = new List<TrakHoundTimeSeriesLatestValue>();
                            values[key] = seriesValues;
                            results[key] = new TrakHoundTimeSeriesLatestResult { Measurement = measurement, Tags = new Dictionary<string, string>(group.Tags) };
                        }

                        seriesValues.Add(new TrakHoundTimeSeriesLatestValue { Field = name, Timestamp = new DateTimeOffset(ParseTime(row[_timeColumn].GetString()), TimeSpan.Zero), Value = value, Quality = quality });
                    }
                }

                foreach (var result in results) result.Value.Values = values[result.Key].ToArray();

                return Ok(results.Values.ToArray());
            });
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            return Run(async () =>
            {
                var schema = await GetSchema(query.Measurement, cancellationToken);
                if (schema == null) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                message = TrakHoundTimeSeriesValidation.ValidateTypes(query, field => schema.Fields.TryGetValue(field, out var type) ? type : null);
                if (message != null) return BadRequest<TrakHoundTimeSeriesResult[]>(message);

                if (!HasTags(schema, query.Tags)) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                // Series key => Tags and Interval start => Column => Value
                var values = new Dictionary<string, (Dictionary<string, string> Tags, Dictionary<long, Dictionary<string, object>> Rows)>();
                var pointsRemaining = _maxTimeWeightedPoints;

                // A Field without a type has never had a Value to calculate
                foreach (var field in query.Fields.Where(o => schema.Fields.TryGetValue(o, out var type) && type != null).Distinct())
                {
                    var standard = query.Functions.Where(o => !TrakHoundTimeSeriesAggregates.IsTimeWeighted(o)).Distinct().ToArray();
                    if (standard.Length > 0) await ReadStandardAggregates(query, schema, field, standard, values, cancellationToken);

                    var timeWeighted = query.Functions.Where(TrakHoundTimeSeriesAggregates.IsTimeWeighted).Distinct().ToArray();
                    if (timeWeighted.Length > 0) pointsRemaining -= await ReadTimeWeightedAggregates(query, schema, field, timeWeighted, pointsRemaining, values, cancellationToken);
                }

                var results = values.OrderBy(o => o.Key, StringComparer.Ordinal)
                    .Select(o => TrakHoundTimeSeriesAggregates.CreateResult(query, o.Value.Tags, o.Value.Rows))
                    .Where(o => o != null)
                    .ToArray();

                return Ok(results);
            });
        }

        public Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags) ?? TrakHoundTimeSeriesValidation.ValidateRange(start, stop);
            if (message != null) return Task.FromResult(BadRequest(message));

            // InfluxDB 3 can't delete Points, only a whole Table or Database
            return Task.FromResult(TrakHoundServiceResponse.RouteNotConfigured("InfluxDB 3 can't delete Points. Use a retention period"));
        }

        protected override void OnDisposed()
        {
            _client.Dispose();
        }


        #region "Writing"

        private async Task SendLines(string lines, CancellationToken cancellationToken)
        {
            // Without accept_partial a batch with a bad line doesn't write the good lines around it
            using var content = new StringContent(lines, Encoding.UTF8, "text/plain");
            using var response = await _client.PostAsync($"api/v3/write_lp?db={Uri.EscapeDataString(_database)}&precision=nanosecond&accept_partial=false", content, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateException(response, cancellationToken);
        }

        private async Task<Dictionary<(string Measurement, string Field), TrakHoundTimeSeriesFieldType>> GetStoredTypes(List<TrakHoundTimeSeriesPoint> points, bool allPoints, CancellationToken cancellationToken)
        {
            var output = new Dictionary<(string, string), TrakHoundTimeSeriesFieldType>();

            var measurements = points.Where(point => allPoints || point.Fields.Values.Any(value => TrakHoundTimeSeriesValidation.GetFieldValue(value, out _) is long)).Select(o => o.Measurement).Distinct();
            foreach (var measurement in measurements)
            {
                var schema = await GetSchema(measurement, cancellationToken);
                if (schema == null) continue;

                foreach (var field in schema.Fields.Where(o => o.Value != null)) output[(measurement, field.Key)] = field.Value.Value;
            }

            return output;
        }

        private static void AppendLine(StringBuilder body, TrakHoundTimeSeriesPoint point, Dictionary<(string Measurement, string Field), TrakHoundTimeSeriesFieldType> types)
        {
            body.Append(point.Measurement);

            if (point.Tags != null)
            {
                foreach (var tag in point.Tags.OrderBy(o => o.Key, StringComparer.Ordinal))
                {
                    body.Append(',').Append(tag.Key).Append('=').Append(EscapeTagValue(tag.Value));
                }
            }

            var separator = ' ';
            foreach (var field in point.Fields)
            {
                var hasValue = !TrakHoundTimeSeriesValidation.IsNoValue(field.Value);
                var quality = hasValue ? (int)point.GetQuality(field.Key) : _qualityNoValue;

                body.Append(separator).Append(_qualityPrefix).Append(field.Key).Append('=').Append(quality).Append('i');
                separator = ',';

                if (!hasValue) continue;

                body.Append(',').Append(field.Key).Append('=');

                var type = types[(point.Measurement, field.Key)];
                var value = TrakHoundTimeSeriesValidation.ConvertFieldValue(TrakHoundTimeSeriesValidation.GetFieldValue(field.Value, out _), type);
                switch (type)
                {
                    case TrakHoundTimeSeriesFieldType.Double: body.Append(((double)value).ToString("R", CultureInfo.InvariantCulture)); break;
                    case TrakHoundTimeSeriesFieldType.Long: body.Append(((long)value).ToString(CultureInfo.InvariantCulture)).Append('i'); break;
                    case TrakHoundTimeSeriesFieldType.Bool: body.Append((bool)value ? 't' : 'f'); break;
                    default: body.Append('"').Append(((string)value).Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); break;
                }
            }

            body.Append(' ').Append((point.Timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100).Append('\n');
        }

        private static string EscapeTagValue(string value) => value.Replace(",", "\\,").Replace("=", "\\=").Replace(" ", "\\ ");

        #endregion

        #region "Reading"

        private async Task ReadStandardAggregates(TrakHoundTimeSeriesAggregateQuery query, Schema schema, string field, TrakHoundTimeSeriesAggregate[] functions, Dictionary<string, (Dictionary<string, string> Tags, Dictionary<long, Dictionary<string, object>> Rows)> values, CancellationToken cancellationToken)
        {
            var column = Quote(field);
            var parameters = new Dictionary<string, string>();
            var filter = GetTagsFilter(query.Tags, parameters);
            var bad = GetQualityFilter(query, schema, field);
            var select = string.Join(", ", functions.Select((o, i) => $"{GetAggregateSql(o, column)} as c{i}"));
            var tags = string.Concat(schema.Tags.Select(o => $"{Quote(o)}, "));
            var groups = string.Join(", ", Enumerable.Range(1, schema.Tags.Count + 1));

            var sql = $@"select {tags}date_bin(interval '{query.Interval.Ticks * 100} nanoseconds', time, {_origin}) as bucket, {select}
from {Quote(query.Measurement)}
where {filter} and {column} is not null and time >= {Literal(query.Start)} and time < {Literal(query.Stop)} {bad}
group by {groups}";

            var type = schema.Fields[field].Value;
            foreach (var row in await Query(sql, parameters, cancellationToken))
            {
                var bucket = ParseTime(row["bucket"].GetString());
                var rows = GetRows(values, GetTags(row, schema));
                for (var i = 0; i < functions.Length; i++)
                {
                    if (!row.TryGetValue($"c{i}", out var value)) continue;
                    GetRow(rows, bucket)[TrakHoundTimeSeriesAggregateQuery.GetColumn(field, functions[i])] = GetAggregateValue(functions[i], type, value);
                }
            }
        }

        // SQL here would need a lateral join for the Intervals, which InfluxDB's SQL doesn't have, so these read the Points of the range
        private async Task<int> ReadTimeWeightedAggregates(TrakHoundTimeSeriesAggregateQuery query, Schema schema, string field, TrakHoundTimeSeriesAggregate[] functions, int maxPoints, Dictionary<string, (Dictionary<string, string> Tags, Dictionary<long, Dictionary<string, object>> Rows)> values, CancellationToken cancellationToken)
        {
            var hasField = GetHasField(schema, field);
            var parameters = new Dictionary<string, string>();
            var filter = GetTagsFilter(query.Tags, parameters);
            var fields = new[] { field };

            var rows = await QueryTop(query.Measurement, schema, fields, $"{filter} and {hasField} and time < {Literal(query.Start)}", "desc", 1, parameters, cancellationToken);

            // One more than can be used, so the Query itself limits the memory and going over the limit can be told apart from reaching it
            var remaining = Math.Max(0, maxPoints - rows.Count);
            rows.AddRange(await Query($"select {GetSelect(schema, fields)} from {Quote(query.Measurement)} where {filter} and {hasField} and time >= {Literal(query.Start)} and time < {Literal(query.Stop)} order by time limit {remaining + 1}", parameters, cancellationToken));

            if (rows.Count > maxPoints)
            {
                throw new InfluxException(400, $"TimeWeightedMean and DurationInState read Points into memory on InfluxDB, and this request needs more than the limit of {_maxTimeWeightedPoints:N0}. Use a shorter range, fewer Fields or fewer Series, or raise maxTimeWeightedPoints");
            }

            var state = TrakHoundTimeSeriesValidation.GetFieldValue(query.State, out _);

            foreach (var series in GroupBySeries(rows, schema))
            {
                var points = new List<(long, object, TrakHoundTimeSeriesQuality)>();
                foreach (var row in series.Rows)
                {
                    if (ReadField(row, schema, field, out var value, out var quality)) points.Add((ParseTime(row[_timeColumn].GetString()), value, quality));
                }

                var output = GetRows(values, series.Tags);

                foreach (var function in functions)
                {
                    var name = TrakHoundTimeSeriesAggregateQuery.GetColumn(field, function);
                    foreach (var value in TrakHoundTimeSeriesAggregates.CalculateTimeWeighted(query, function, points, state))
                    {
                        GetRow(output, value.Key)[name] = value.Value;
                    }
                }
            }

            return rows.Count;
        }

        private static Dictionary<long, Dictionary<string, object>> GetRows(Dictionary<string, (Dictionary<string, string> Tags, Dictionary<long, Dictionary<string, object>> Rows)> values, Dictionary<string, string> tags)
        {
            var key = GetSeriesKey(tags);
            if (!values.TryGetValue(key, out var series))
            {
                series = (tags, new Dictionary<long, Dictionary<string, object>>());
                values[key] = series;
            }

            return series.Rows;
        }

        private static Dictionary<string, object> GetRow(Dictionary<long, Dictionary<string, object>> values, long bucket)
        {
            if (!values.TryGetValue(bucket, out var row))
            {
                row = new Dictionary<string, object>();
                values[bucket] = row;
            }

            return row;
        }

        private static string GetAggregateSql(TrakHoundTimeSeriesAggregate function, string column)
        {
            switch (function)
            {
                case TrakHoundTimeSeriesAggregate.Mean: return $"avg({column})";
                case TrakHoundTimeSeriesAggregate.Min: return $"min({column})";
                case TrakHoundTimeSeriesAggregate.Max: return $"max({column})";
                case TrakHoundTimeSeriesAggregate.Sum: return $"sum({column})";
                case TrakHoundTimeSeriesAggregate.Count: return $"count({column})";
                case TrakHoundTimeSeriesAggregate.First: return $"first_value({column} order by time)";
                default: return $"last_value({column} order by time)";
            }
        }

        private static object GetAggregateValue(TrakHoundTimeSeriesAggregate function, TrakHoundTimeSeriesFieldType type, JsonElement value)
        {
            switch (function)
            {
                case TrakHoundTimeSeriesAggregate.Mean: return value.GetDouble();
                case TrakHoundTimeSeriesAggregate.Count: return value.GetInt64();
                default: return GetValue(value, type);
            }
        }

        private TrakHoundTimeSeriesResult CreateResult(string measurement, Dictionary<string, string> series, Schema schema, string[] names, bool onlyUsedFields, List<Dictionary<string, JsonElement>> rows, bool hasMore)
        {
            var used = names.Where(name => !onlyUsedFields || rows.Any(row => ReadField(row, schema, name, out _, out _))).ToArray();
            var columns = TrakHoundTimeSeriesResult.GetColumns(used);

            var output = rows.Select(row =>
            {
                var values = new object[columns.Length];
                values[0] = new DateTimeOffset(ParseTime(row[_timeColumn].GetString()), TimeSpan.Zero);
                for (var i = 0; i < used.Length; i++)
                {
                    if (!ReadField(row, schema, used[i], out var value, out var quality)) continue;

                    values[i * 2 + 1] = value;
                    values[i * 2 + 2] = quality.ToString();
                }

                return values;
            }).ToArray();

            return new TrakHoundTimeSeriesResult { Measurement = measurement, Tags = new Dictionary<string, string>(series), Columns = columns, Rows = output, HasMore = hasMore };
        }

        /// <summary>
        /// Reads a Field of a row. Returns false if the row doesn't have the Field
        /// </summary>
        private static bool ReadField(Dictionary<string, JsonElement> row, Schema schema, string field, out object value, out TrakHoundTimeSeriesQuality quality)
        {
            value = null;
            quality = TrakHoundTimeSeriesQuality.Good;

            if (!schema.Fields.TryGetValue(field, out var type)) return false;

            var hasQuality = row.TryGetValue($"{_qualityPrefix}{field}", out var storedQuality);
            var hasValue = row.TryGetValue(field, out var storedValue);
            if (!hasQuality && !hasValue) return false;

            if (hasQuality)
            {
                var code = storedQuality.GetInt64();
                if (code == _qualityNoValue)
                {
                    quality = TrakHoundTimeSeriesQuality.Bad;
                    return true;
                }

                quality = (TrakHoundTimeSeriesQuality)code;
            }

            if (hasValue && type != null) value = GetValue(storedValue, type.Value);
            return true;
        }

        private static IEnumerable<string> GetFieldColumns(Schema schema, string field)
        {
            if (schema.Fields.TryGetValue(field, out var type) && type != null) yield return Quote(field);
            if (schema.Qualities.Contains(field)) yield return QualityColumn(field);
        }

        /// <summary>
        /// Gets the condition for a row that has a Field
        /// </summary>
        private static string GetHasField(Schema schema, string field) => "(" + string.Join(" or ", GetFieldColumns(schema, field).Select(o => $"{o} is not null")) + ")";

        /// <summary>
        /// Gets the condition that leaves out Bad Values, and Fields without a Value
        /// </summary>
        private static string GetQualityFilter(TrakHoundTimeSeriesAggregateQuery query, Schema schema, string field)
        {
            if (!schema.Qualities.Contains(field)) return "";

            var column = QualityColumn(field);
            return query.IncludeBadQuality
                ? $"and ({column} is null or {column} <> {_qualityNoValue})"
                : $"and ({column} is null or {column} not in ({(int)TrakHoundTimeSeriesQuality.Bad}, {_qualityNoValue}))";
        }

        private static string GetSelect(Schema schema, string[] fields) => string.Join(", ", schema.Tags.Select(Quote).Concat(new[] { _timeColumn }).Concat(fields.SelectMany(o => GetFieldColumns(schema, o))));

        /// <summary>
        /// Reads the first Points of each Series in one Query. One Query per Series was far slower, since each one scans the data (about 230 ms each for 1,000,000 Points)
        /// </summary>
        private async Task<List<Dictionary<string, JsonElement>>> QueryTop(string measurement, Schema schema, string[] fields, string where, string direction, int take, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            var select = GetSelect(schema, fields);
            var partition = schema.Tags.Count > 0 ? $"partition by {string.Join(", ", schema.Tags.Select(Quote))} " : "";

            var sql = $@"select {select} from (
    select {select}, row_number() over ({partition}order by time {direction}) as trakhound_row
    from {Quote(measurement)} where {where}
) where trakhound_row <= {take} order by time {direction}";

            return await Query(sql, parameters, cancellationToken);
        }

        private static List<(Dictionary<string, string> Tags, List<Dictionary<string, JsonElement>> Rows)> GroupBySeries(List<Dictionary<string, JsonElement>> rows, Schema schema)
        {
            var groups = new Dictionary<string, (Dictionary<string, string> Tags, List<Dictionary<string, JsonElement>> Rows)>();
            foreach (var row in rows)
            {
                var tags = GetTags(row, schema);
                var key = GetSeriesKey(tags);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = (tags, new List<Dictionary<string, JsonElement>>());
                    groups[key] = group;
                }

                group.Rows.Add(row);
            }

            return groups.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Value).ToList();
        }

        private static Dictionary<string, string> GetTags(Dictionary<string, JsonElement> row, Schema schema)
        {
            var tags = new Dictionary<string, string>();
            foreach (var tag in schema.Tags)
            {
                if (row.TryGetValue(tag, out var value) && value.ValueKind == JsonValueKind.String) tags[tag] = value.GetString();
            }

            return tags;
        }

        /// <summary>
        /// Returns whether a Measurement has every Tag. A Query can't name a Column that doesn't exist, and no Series can match
        /// </summary>
        private static bool HasTags(Schema schema, IReadOnlyDictionary<string, string[]> tags) => tags == null || tags.Keys.All(schema.Tags.Contains);

        /// <summary>
        /// Selects the Series that have one of the Values of each of the Tags, and adds the Parameters it uses
        /// </summary>
        private static string GetTagsFilter(IReadOnlyDictionary<string, string[]> tags, Dictionary<string, string> parameters)
        {
            if (tags == null || tags.Count < 1) return "true";

            var conditions = new List<string>();
            foreach (var tag in tags)
            {
                var names = new List<string>();
                foreach (var value in tag.Value)
                {
                    var name = $"t{parameters.Count}";
                    parameters[name] = value;
                    names.Add($"${name}");
                }

                conditions.Add(names.Count == 1 ? $"{Quote(tag.Key)} = {names[0]}" : $"{Quote(tag.Key)} in ({string.Join(", ", names)})");
            }

            return string.Join(" and ", conditions);
        }

        private static object GetValue(JsonElement value, TrakHoundTimeSeriesFieldType type)
        {
            switch (type)
            {
                case TrakHoundTimeSeriesFieldType.Double: return value.GetDouble();
                case TrakHoundTimeSeriesFieldType.Long: return value.GetInt64();
                case TrakHoundTimeSeriesFieldType.Bool: return value.GetBoolean();
                default: return value.GetString();
            }
        }

        private static long ParseTime(string value)
        {
            // Times are UTC without an offset, and can have more decimal places than DateTime keeps
            var dot = value.IndexOf('.');
            if (dot > 0 && value.Length > dot + 8) value = value.Substring(0, dot + 8);
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Ticks;
        }

        private static string Literal(DateTimeOffset timestamp) => $"'{timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)}Z'";

        private static string QualityColumn(string field) => Quote($"{_qualityPrefix}{field}");

        private static string Quote(string name) => $"\"{name}\"";

        /// <summary>
        /// Gets the Series that have all of the Tags, in a consistent order
        /// </summary>
        private async Task<List<Dictionary<string, string>>> GetSeries(string measurement, Schema schema, IReadOnlyDictionary<string, string[]> tags, CancellationToken cancellationToken)
        {
            var output = new List<Dictionary<string, string>>();

            if (!HasTags(schema, tags)) return output;

            if (schema.Tags.Count < 1)
            {
                var any = await Query($"select time from {Quote(measurement)} limit 1", null, cancellationToken);
                if (any.Count > 0 && (tags == null || tags.Count < 1)) output.Add(new Dictionary<string, string>());
                return output;
            }

            var parameters = new Dictionary<string, string>();
            var rows = await Query($"select distinct {string.Join(", ", schema.Tags.Select(Quote))} from {Quote(measurement)} where {GetTagsFilter(tags, parameters)}", parameters, cancellationToken);

            foreach (var row in rows) output.Add(GetTags(row, schema));

            return output.OrderBy(GetSeriesKey, StringComparer.Ordinal).ToList();
        }

        private static string GetSeriesKey(Dictionary<string, string> tags) => string.Join("\n", tags.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));

        /// <summary>
        /// Gets the Tags and Fields of a Measurement (null if it doesn't exist yet)
        /// </summary>
        private async Task<Schema> GetSchema(string measurement, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, string> { ["measurement"] = measurement };
            var rows = await Query("select column_name, data_type from information_schema.columns where table_schema = 'iox' and table_name = $measurement", parameters, cancellationToken);
            if (rows.Count < 1) return null;

            var schema = new Schema();
            foreach (var row in rows)
            {
                var name = row["column_name"].GetString();
                var type = row["data_type"].GetString();

                if (name == _timeColumn) continue;

                if (type.StartsWith(_tagType))
                {
                    schema.Tags.Add(name);
                }
                else if (name.StartsWith(_qualityPrefix, StringComparison.Ordinal))
                {
                    var field = name.Substring(_qualityPrefix.Length);
                    schema.Qualities.Add(field);
                    schema.Fields.TryAdd(field, null);
                }
                else
                {
                    schema.Fields[name] = GetFieldType(type);
                }
            }

            return schema;
        }

        private static TrakHoundTimeSeriesFieldType GetFieldType(string columnType)
        {
            switch (columnType)
            {
                case "Float64": return TrakHoundTimeSeriesFieldType.Double;
                case "Int64":
                case "UInt64":
                    return TrakHoundTimeSeriesFieldType.Long;
                case "Boolean": return TrakHoundTimeSeriesFieldType.Bool;
                default: return TrakHoundTimeSeriesFieldType.String;
            }
        }

        #endregion

        #region "Requests"

        private async Task<List<Dictionary<string, JsonElement>>> Query(string sql, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["db"] = _database,
                ["q"] = sql,
                ["format"] = "jsonl",
                ["params"] = parameters ?? new Dictionary<string, string>()
            });

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v3/query_sql") { Content = content };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var output = new List<Dictionary<string, JsonElement>>();

            // A database is created by its first write, so until then there is nothing to find
            if (response.StatusCode == HttpStatusCode.NotFound) return output;
            if (!response.IsSuccessStatusCode) throw await CreateException(response, cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (line.Length > 0) output.Add(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line));
            }

            return output;
        }

        private static async Task<InfluxException> CreateException(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            return new InfluxException(GetStatusCode((int)response.StatusCode), $"InfluxDB : {(int)response.StatusCode} : {message}");
        }

        /// <summary>
        /// Gets the Status Code a Service Router should see: errors in the Request aren't sent to the next Target, and a failed login is the same as the server not being available
        /// </summary>
        private static int GetStatusCode(int statusCode)
        {
            switch (statusCode)
            {
                case 400:
                case 413:
                case 422:
                    return 400;
                case 401:
                case 403:
                case 429:
                case 503:
                    return 503;
                case 408:
                case 504:
                    return 504;
                default:
                    return 500;
            }
        }

        private async Task<TrakHoundServiceResponse<TResult>> Run<TResult>(Func<Task<TrakHoundServiceResponse<TResult>>> action)
        {
            try
            {
                return await action();
            }
            catch (InfluxException ex)
            {
                return new TrakHoundServiceResponse<TResult>(default, ex.StatusCode, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return new TrakHoundServiceResponse<TResult>(default, 503, ex.Message);
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                // The HttpClient's own timeout, not a caller that canceled
                return new TrakHoundServiceResponse<TResult>(default, 504, "The request to InfluxDB timed out");
            }
            catch (InvalidOperationException ex) when (_client.BaseAddress == null)
            {
                return new TrakHoundServiceResponse<TResult>(default, 503, $"The Service has no url : {ex.Message}");
            }
        }

        #endregion
    }
}
