// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Npgsql;
using NpgsqlTypes;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TrakHound.Logging;
using TrakHound.Postgres.Service;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Timescale.Service
{
    /// <summary>
    /// A Time Series Service that stores Points in TimescaleDB. A "$series" Table maps Tags to a Series ID, and each Measurement is a
    /// Hypertable with Columns for each Field: its Value, and its Quality in "$q_" and the Field's name. A row has a Field when its Quality
    /// isn't null, so a Field can be recorded without a Value. Field names only have letters, numbers and underscores, so these names can't match one
    /// </summary>
    public class TimescaleTimeSeriesService : TrakHoundService, ITrakHoundTimeSeriesService
    {
        private const string _seriesColumn = "\"$series\"";
        private const string _qualityPrefix = "$q_";
        private const string _origin = "'1970-01-01 00:00:00+00'::timestamptz";
        private const string _defaultSchema = "trakhound_timeseries";
        private const int _maxCachedSeries = 100000;
        private static readonly TimeSpan _availabilityCacheTime = TimeSpan.FromSeconds(60);

        private readonly string _connectionString;
        private readonly string _schema;
        private readonly int _chunkDays;
        private readonly int _compressAfterDays;
        private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?>> _columns = new();
        private readonly ConcurrentDictionary<string, long> _seriesIds = new();
        private readonly SemaphoreSlim _initializeLock = new(1, 1);
        private volatile bool _initialized;
        private TrakHoundServiceAvailabilityResponse _availability;
        private DateTime _availabilityTime;


        public TimescaleTimeSeriesService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            _connectionString = PostgresConnectionString.Create(configuration, $"TrakHound-Instance:{InstanceId}");

            var schema = configuration?.GetParameter("schema");
            _schema = Quote(!string.IsNullOrEmpty(schema) ? schema : _defaultSchema);

            _chunkDays = configuration != null && configuration.ParameterExists("chunkDays") ? Math.Max(1, configuration.GetParameter<int>("chunkDays")) : 7;
            _compressAfterDays = configuration != null && configuration.ParameterExists("compressAfterDays") ? configuration.GetParameter<int>("compressAfterDays") : 30;
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var cached = _availability;
            if (cached != null && DateTime.UtcNow - _availabilityTime < _availabilityCacheTime) return Ok(cached);

            var response = new TrakHoundServiceAvailabilityResponse();
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("select extversion from pg_extension where extname = 'timescaledb';", connection);
                var version = await command.ExecuteScalarAsync(cancellationToken) as string;

                response.IsAvailable = version != null;
                response.Message = version != null ? $"TimescaleDB {version}" : "The timescaledb extension isn't enabled in this database";
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
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
            return Run<IEnumerable<string>>(cancellationToken, async connection =>
            {
                var measurements = new List<string>();
                await using (var command = new NpgsqlCommand($"select distinct measurement from {SeriesTable} order by 1;", connection))
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken)) measurements.Add(reader.GetString(0));
                }

                // Series rows outlive their Points, since other Instances may have their IDs cached
                var output = new List<string>();
                foreach (var measurement in measurements)
                {
                    await using var command = new NpgsqlCommand($"select exists (select 1 from {Table(measurement)});", connection);
                    if ((bool)await command.ExecuteScalarAsync(cancellationToken)) output.Add(measurement);
                }

                return Ok<IEnumerable<string>>(output);
            });
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeries>>(message));

            return Run<IEnumerable<TrakHoundTimeSeries>>(cancellationToken, async connection =>
            {
                var output = new List<TrakHoundTimeSeries>();
                if (await GetColumns(connection, measurement, true, cancellationToken) == null) return Ok<IEnumerable<TrakHoundTimeSeries>>(output);

                var query = $@"select s.tags from {SeriesTable} s
where s.measurement = @measurement and s.tags @> @tags and exists (select 1 from {Table(measurement)} p where p.{_seriesColumn} = s.id)
order by s.tags_key offset @skip limit @take;";

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("measurement", measurement);
                command.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb, ToJson(tags));
                command.Parameters.AddWithValue("skip", Math.Max(skip, 0));
                command.Parameters.AddWithValue("take", Math.Max(take, 0));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    output.Add(new TrakHoundTimeSeries { Measurement = measurement, Tags = FromJson(reader.GetString(0)) });
                }

                return Ok<IEnumerable<TrakHoundTimeSeries>>(output);
            });
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement);
            if (message != null) return Task.FromResult(BadRequest<IEnumerable<TrakHoundTimeSeriesField>>(message));

            return Run<IEnumerable<TrakHoundTimeSeriesField>>(cancellationToken, async connection =>
            {
                var columns = await GetColumns(connection, measurement, true, cancellationToken);
                var fields = columns?.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new TrakHoundTimeSeriesField { Name = o.Key, Type = o.Value }).ToList();
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
            }

            var response = await Run<bool>(cancellationToken, async connection =>
            {
                foreach (var group in items.GroupBy(o => o.Measurement))
                {
                    var failed = await WriteMeasurement(connection, group.Key, group.ToList(), cancellationToken);
                    if (failed != null) return BadRequest<bool>(failed);
                }

                return Ok(true);
            });

            return new TrakHoundServiceResponse(response.StatusCode, response.Message);
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            return Run(cancellationToken, async connection =>
            {
                var columns = await GetColumns(connection, query.Measurement, true, cancellationToken);
                if (columns == null) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                var series = await GetSeries(connection, query.Measurement, query.Tags, cancellationToken);
                if (series.Count < 1) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                var names = query.Fields ?? columns.Keys.OrderBy(o => o, StringComparer.Ordinal).ToArray();
                var existing = names.Where(columns.ContainsKey).ToArray();
                if (existing.Length < 1) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                var select = GetFieldSelect(names, columns);
                var hasField = string.Join(" or ", existing.Select(o => $"p.{QualityColumn(o)} is not null"));
                var direction = query.Descending ? "desc" : "asc";

                var sql = new StringBuilder();
                sql.Append($@"select i.id, 0 as part, p.""time"", {select} from unnest(@ids) i(id) cross join lateral (
    select * from {Table(query.Measurement)} p
    where p.{_seriesColumn} = i.id and p.""time"" >= @start and p.""time"" < @stop and ({hasField})
    order by p.""time"" {direction} limit @take
) p");

                if (query.IncludePrevious)
                {
                    sql.Append($@"
union all
select i.id, 1 as part, p.""time"", {select} from unnest(@ids) i(id) cross join lateral (
    select * from {Table(query.Measurement)} p
    where p.{_seriesColumn} = i.id and p.""time"" < @start and ({hasField})
    order by p.""time"" desc limit 1
) p");
                }

                sql.Append($@"
order by 1, 3 {direction};");

                await using var command = new NpgsqlCommand(sql.ToString(), connection);
                command.Parameters.AddWithValue("ids", series.Keys.ToArray());
                command.Parameters.AddWithValue("start", query.Start.UtcDateTime);
                command.Parameters.AddWithValue("stop", query.Stop.UtcDateTime);

                // One more than Take, which says whether Take left out Points
                var take = TrakHoundTimeSeriesValidation.GetTake(query.Take);
                command.Parameters.AddWithValue("take", take + 1);

                return Ok(await ReadResults(command, query.Measurement, series, names, query.Fields == null, take, cancellationToken));
            });
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default)
        {
            var fieldNames = fields?.ToArray();
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTagFilter(tags) ?? TrakHoundTimeSeriesValidation.ValidateFields(fieldNames);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesLatestResult[]>(message));

            return Run(cancellationToken, async connection =>
            {
                var columns = await GetColumns(connection, measurement, true, cancellationToken);
                if (columns == null) return Ok(Array.Empty<TrakHoundTimeSeriesLatestResult>());

                var series = await GetSeries(connection, measurement, tags, cancellationToken);
                if (series.Count < 1) return Ok(Array.Empty<TrakHoundTimeSeriesLatestResult>());

                var names = fieldNames ?? columns.Keys.OrderBy(o => o, StringComparer.Ordinal).ToArray();
                var values = new Dictionary<long, List<TrakHoundTimeSeriesLatestValue>>();

                // One Query for each Field, since each Field's newest row can be a different one and the Columns have different types
                foreach (var name in names.Where(columns.ContainsKey).Distinct())
                {
                    var quality = QualityColumn(name);
                    var value = columns[name] != null ? $"p.{Quote(name)}" : "null";

                    var sql = $@"select i.id, p.""time"", {value}, p.{quality} from unnest(@ids) i(id) cross join lateral (
    select * from {Table(measurement)} p
    where p.{_seriesColumn} = i.id and p.{quality} is not null
    order by p.""time"" desc limit 1
) p;";

                    await using var command = new NpgsqlCommand(sql, connection);
                    command.Parameters.AddWithValue("ids", series.Keys.ToArray());

                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var id = reader.GetInt64(0);
                        if (!values.TryGetValue(id, out var seriesValues))
                        {
                            seriesValues = new List<TrakHoundTimeSeriesLatestValue>();
                            values[id] = seriesValues;
                        }

                        seriesValues.Add(new TrakHoundTimeSeriesLatestValue
                        {
                            Field = name,
                            Timestamp = new DateTimeOffset(reader.GetFieldValue<DateTime>(1).Ticks, TimeSpan.Zero),
                            Value = reader.IsDBNull(2) ? null : reader.GetValue(2),
                            Quality = (TrakHoundTimeSeriesQuality)reader.GetInt16(3)
                        });
                    }
                }

                var results = series.Where(o => values.ContainsKey(o.Key)).Select(o => new TrakHoundTimeSeriesLatestResult
                {
                    Measurement = measurement,
                    Tags = new Dictionary<string, string>(o.Value),
                    Values = values[o.Key].ToArray()
                });

                return Ok(results.ToArray());
            });
        }

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateQuery(query);
            if (message != null) return Task.FromResult(BadRequest<TrakHoundTimeSeriesResult[]>(message));

            return Run(cancellationToken, async connection =>
            {
                var columns = await GetColumns(connection, query.Measurement, true, cancellationToken);
                if (columns == null) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                message = TrakHoundTimeSeriesValidation.ValidateTypes(query, field => columns.TryGetValue(field, out var type) ? type : null);
                if (message != null) return BadRequest<TrakHoundTimeSeriesResult[]>(message);

                var series = await GetSeries(connection, query.Measurement, query.Tags, cancellationToken);
                if (series.Count < 1) return Ok(Array.Empty<TrakHoundTimeSeriesResult>());

                // Series ID => Interval start => Column => Value
                var values = new Dictionary<long, Dictionary<long, Dictionary<string, object>>>();

                // A Field without a type has never had a Value to calculate
                foreach (var field in query.Fields.Where(o => columns.TryGetValue(o, out var type) && type != null).Distinct())
                {
                    var standard = query.Functions.Where(o => !TrakHoundTimeSeriesAggregates.IsTimeWeighted(o)).Distinct().ToArray();
                    if (standard.Length > 0) await ReadStandardAggregates(connection, query, series, field, columns[field].Value, standard, values, cancellationToken);

                    foreach (var function in query.Functions.Where(TrakHoundTimeSeriesAggregates.IsTimeWeighted).Distinct())
                    {
                        await ReadTimeWeightedAggregate(connection, query, series, field, function, values, cancellationToken);
                    }
                }

                var results = new List<TrakHoundTimeSeriesResult>();
                foreach (var item in series)
                {
                    values.TryGetValue(item.Key, out var seriesValues);
                    var result = TrakHoundTimeSeriesAggregates.CreateResult(query, item.Value, seriesValues);
                    if (result != null) results.Add(result);
                }

                return Ok(results.ToArray());
            });
        }

        public async Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default)
        {
            var message = TrakHoundTimeSeriesValidation.ValidateMeasurement(measurement) ?? TrakHoundTimeSeriesValidation.ValidateTags(tags) ?? TrakHoundTimeSeriesValidation.ValidateRange(start, stop);
            if (message != null) return BadRequest(message);

            var response = await Run<bool>(cancellationToken, async connection =>
            {
                if (await GetColumns(connection, measurement, false, cancellationToken) == null) return Ok(true);

                var series = await GetSeries(connection, measurement, TrakHoundTimeSeriesQuery.GetTagFilter(tags), cancellationToken);
                if (series.Count < 1) return Ok(true);

                await using var command = new NpgsqlCommand($@"delete from {Table(measurement)} where {_seriesColumn} = any(@ids) and ""time"" >= @start and ""time"" < @stop;", connection);
                command.Parameters.AddWithValue("ids", series.Keys.ToArray());
                command.Parameters.AddWithValue("start", start.UtcDateTime);
                command.Parameters.AddWithValue("stop", stop.UtcDateTime);
                await command.ExecuteNonQueryAsync(cancellationToken);

                return Ok(true);
            });

            return new TrakHoundServiceResponse(response.StatusCode, response.Message);
        }

        protected override void OnDisposed()
        {
            _initializeLock.Dispose();
        }


        #region "Writing"

        private async Task<string> WriteMeasurement(NpgsqlConnection connection, string measurement, List<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken)
        {
            var (message, types) = await EnsureColumns(connection, measurement, points, cancellationToken);
            if (message != null) return message;

            var seriesIds = await EnsureSeries(connection, measurement, points, cancellationToken);

            // An upsert can't touch a row twice, so Points for the same time are merged first (later Points win, as they would if written one at a time)
            var rows = new Dictionary<(long Series, DateTime Time), Dictionary<string, (object Value, short Quality)>>();
            foreach (var point in points)
            {
                var key = (seriesIds[GetTagsKey(point.Tags)], point.Timestamp.UtcDateTime);
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new Dictionary<string, (object Value, short Quality)>();
                    rows[key] = row;
                }

                foreach (var field in point.Fields)
                {
                    var value = TrakHoundTimeSeriesValidation.IsNoValue(field.Value) ? null : TrakHoundTimeSeriesValidation.ConvertFieldValue(TrakHoundTimeSeriesValidation.GetFieldValue(field.Value, out _), types[field.Key]);
                    row[field.Key] = (value, (short)point.GetQuality(field.Key));
                }
            }

            var names = points.SelectMany(o => o.Fields.Keys).Distinct().ToArray();
            var parameters = new List<string> { "@time", "@series" };
            var aliases = new List<string> { "\"time\"", _seriesColumn };
            var updates = new List<string>();

            await using var command = new NpgsqlCommand { Connection = connection };
            command.Parameters.Add(new NpgsqlParameter("time", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = rows.Keys.Select(o => o.Time).ToArray() });
            command.Parameters.AddWithValue("series", rows.Keys.Select(o => o.Series).ToArray());

            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var quality = QualityColumn(name);

                parameters.Add($"@q{i}");
                aliases.Add(quality);
                updates.Add($"{quality} = coalesce(excluded.{quality}, t.{quality})");
                command.Parameters.Add(new NpgsqlParameter($"q{i}", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = rows.Values.Select(o => o.TryGetValue(name, out var field) ? field.Quality : (short?)null).ToArray() });

                // A Field that has never had a Value has no Column for one yet
                if (!types.TryGetValue(name, out var type)) continue;

                parameters.Add($"@f{i}");
                aliases.Add(Quote(name));
                updates.Add($"{Quote(name)} = case when excluded.{quality} is not null then excluded.{Quote(name)} else t.{Quote(name)} end");
                command.Parameters.Add(CreateFieldParameter($"f{i}", type, rows.Values.Select(o => o.TryGetValue(name, out var field) ? field.Value : null)));
            }

            var columnNames = string.Join(", ", aliases);
            command.CommandText = $@"insert into {Table(measurement)} as t ({columnNames})
select * from unnest({string.Join(", ", parameters)}) as x({columnNames})
on conflict ({_seriesColumn}, ""time"") do update set {string.Join(", ", updates)};";

            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }

        private static NpgsqlParameter CreateFieldParameter(string name, TrakHoundTimeSeriesFieldType type, IEnumerable<object> values)
        {
            switch (type)
            {
                case TrakHoundTimeSeriesFieldType.Double:
                    return new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = values.Select(o => (double?)o).ToArray() };
                case TrakHoundTimeSeriesFieldType.Long:
                    return new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = values.Select(o => (long?)o).ToArray() };
                case TrakHoundTimeSeriesFieldType.Bool:
                    return new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = values.Select(o => (bool?)o).ToArray() };
                default:
                    return new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.Select(o => (string)o).ToArray() };
            }
        }

        /// <summary>
        /// Creates the Hypertable and the Columns the Fields need, and returns the type each Field is written as. Returns a message if a Value can't be written
        /// </summary>
        private async Task<(string Message, Dictionary<string, TrakHoundTimeSeriesFieldType> Types)> EnsureColumns(NpgsqlConnection connection, string measurement, List<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken)
        {
            var columns = await GetColumns(connection, measurement, false, cancellationToken);

            // Another Instance may have added a Field since these Columns were read
            if (!HasColumns(points, columns)) columns = await GetColumns(connection, measurement, true, cancellationToken);

            var message = GetTypes(points, columns, out var types);
            if (message != null) return (message, null);

            if (HasColumns(points, columns)) return (null, types);

            var created = false;
            await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
            {
                // Writers wait here, so only one adds a Column or creates the Hypertable, and the others then see its type
                await using (var command = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key, 0));", connection, transaction))
                {
                    command.Parameters.AddWithValue("key", $"trakhound-timeseries:{_schema}.{measurement}");
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                columns = await ReadColumns(connection, transaction, measurement, cancellationToken);
                message = GetTypes(points, columns, out types);
                if (message != null) return (message, null);

                if (columns == null)
                {
                    created = true;
                    var sql = $@"create table {Table(measurement)} (""time"" timestamptz not null, {_seriesColumn} bigint not null, primary key ({_seriesColumn}, ""time""));
select create_hypertable(@table, 'time', chunk_time_interval => make_interval(days => @chunkDays), if_not_exists => true);";

                    await using var command = new NpgsqlCommand(sql, connection, transaction);
                    command.Parameters.AddWithValue("table", Table(measurement));
                    command.Parameters.AddWithValue("chunkDays", _chunkDays);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var field in points.SelectMany(o => o.Fields.Keys).Distinct().Where(o => columns == null || !columns.ContainsKey(o)))
                {
                    await using var command = new NpgsqlCommand($"alter table {Table(measurement)} add column if not exists {QualityColumn(field)} smallint;", connection, transaction);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var field in types.Where(o => columns == null || !columns.TryGetValue(o.Key, out var type) || type == null))
                {
                    await using var command = new NpgsqlCommand($"alter table {Table(measurement)} add column if not exists {Quote(field.Key)} {GetColumnType(field.Value)};", connection, transaction);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }

            if (created) await EnableCompression(connection, measurement, cancellationToken);

            _columns.TryRemove(measurement, out _);
            return (null, types);
        }

        private async Task EnableCompression(NpgsqlConnection connection, string measurement, CancellationToken cancellationToken)
        {
            if (_compressAfterDays < 1) return;

            try
            {
                var sql = $@"alter table {Table(measurement)} set (timescaledb.compress, timescaledb.compress_segmentby = '""$series""', timescaledb.compress_orderby = '""time"" desc');
select add_compression_policy(@table, make_interval(days => @days), if_not_exists => true);";

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("table", Table(measurement));
                command.Parameters.AddWithValue("days", _compressAfterDays);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException ex)
            {
                // The Apache licensed edition of TimescaleDB has no compression. The Hypertable already exists, so the Points are still stored
                Log(TrakHoundLogLevel.Warning, $"Compression isn't available for {measurement} : {ex.MessageText}");
            }
        }

        private async Task<Dictionary<string, long>> EnsureSeries(NpgsqlConnection connection, string measurement, List<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken)
        {
            var output = new Dictionary<string, long>();
            var missing = new Dictionary<string, string>();

            foreach (var point in points)
            {
                var key = GetTagsKey(point.Tags);
                if (output.ContainsKey(key) || missing.ContainsKey(key)) continue;

                if (_seriesIds.TryGetValue($"{measurement}\n{key}", out var id)) output[key] = id;
                else missing[key] = ToJson(point.Tags);
            }

            if (missing.Count < 1) return output;

            var keys = missing.Keys.ToArray();
            var sql = $@"insert into {SeriesTable} (measurement, tags_key, tags)
select @measurement, k, t from unnest(@keys, @tags::jsonb[]) as x(k, t)
on conflict (measurement, tags_key) do nothing;
select tags_key, id from {SeriesTable} where measurement = @measurement and tags_key = any(@keys);";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("measurement", measurement);
            command.Parameters.AddWithValue("keys", keys);
            command.Parameters.AddWithValue("tags", keys.Select(o => missing[o]).ToArray());

            // Tags with unbounded values would otherwise grow this without limit
            if (_seriesIds.Count > _maxCachedSeries) _seriesIds.Clear();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                var id = reader.GetInt64(1);
                output[key] = id;
                _seriesIds[$"{measurement}\n{key}"] = id;
            }

            return output;
        }

        /// <summary>
        /// Returns whether a Measurement has the Columns the Points need
        /// </summary>
        private static bool HasColumns(List<TrakHoundTimeSeriesPoint> points, IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?> columns)
        {
            if (columns == null) return false;

            return points.All(point => point.Fields.All(field => columns.TryGetValue(field.Key, out var type) && (type != null || TrakHoundTimeSeriesValidation.IsNoValue(field.Value))));
        }

        private static string GetTypes(List<TrakHoundTimeSeriesPoint> points, IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?> columns, out Dictionary<string, TrakHoundTimeSeriesFieldType> types)
        {
            var message = TrakHoundTimeSeriesValidation.GetWriteTypes(points, (_, field) => columns != null && columns.TryGetValue(field, out var type) ? type : null, out var writeTypes);
            types = writeTypes?.ToDictionary(o => o.Key.Field, o => o.Value);
            return message;
        }

        #endregion

        #region "Reading"

        private async Task ReadStandardAggregates(NpgsqlConnection connection, TrakHoundTimeSeriesAggregateQuery query, Dictionary<long, Dictionary<string, string>> series, string field, TrakHoundTimeSeriesFieldType type, TrakHoundTimeSeriesAggregate[] functions, Dictionary<long, Dictionary<long, Dictionary<string, object>>> values, CancellationToken cancellationToken)
        {
            var column = Quote(field);
            var select = string.Join(", ", functions.Select(o => GetAggregateSql(o, column, type)));
            var bad = query.IncludeBadQuality ? "" : $"and {QualityColumn(field)} <> {(int)TrakHoundTimeSeriesQuality.Bad}";

            var sql = $@"select {_seriesColumn}, time_bucket(@interval, ""time"", origin => {_origin}) as bucket, {select}
from {Table(query.Measurement)}
where {_seriesColumn} = any(@ids) and ""time"" >= @start and ""time"" < @stop and {column} is not null {bad}
group by 1, 2;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("interval", query.Interval);
            command.Parameters.AddWithValue("ids", series.Keys.ToArray());
            command.Parameters.AddWithValue("start", query.Start.UtcDateTime);
            command.Parameters.AddWithValue("stop", query.Stop.UtcDateTime);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = GetAggregateRow(values, reader.GetInt64(0), reader.GetFieldValue<DateTime>(1));
                for (var i = 0; i < functions.Length; i++)
                {
                    row[TrakHoundTimeSeriesAggregateQuery.GetColumn(field, functions[i])] = reader.IsDBNull(i + 2) ? null : reader.GetValue(i + 2);
                }
            }
        }

        // A Value holds from its Point until the next Point with the Field, but never past Stop or the current time.
        // A skipped Bad Point or a Point without a Value ends the Value before it, and the time until the next Point isn't counted
        private async Task ReadTimeWeightedAggregate(NpgsqlConnection connection, TrakHoundTimeSeriesAggregateQuery query, Dictionary<long, Dictionary<string, string>> series, string field, TrakHoundTimeSeriesAggregate function, Dictionary<long, Dictionary<long, Dictionary<string, object>>> values, CancellationToken cancellationToken)
        {
            var column = Quote(field);
            var table = Table(query.Measurement);
            var quality = QualityColumn(field);
            var bad = query.IncludeBadQuality ? "" : $"and quality <> {(int)TrakHoundTimeSeriesQuality.Bad}";

            var calculation = function == TrakHoundTimeSeriesAggregate.DurationInState
                ? "sum(case when value = @state then x.seconds else 0 end)"
                : "sum(value::double precision * x.seconds) / sum(x.seconds)";

            var sql = $@"with limits as (select @start::timestamptz as range_start, least(@stop::timestamptz, now()) as range_end),
points as (
    select p.{_seriesColumn} as id, p.""time"", p.{column} as value, p.{quality} as quality from {table} p, limits
    where p.{_seriesColumn} = any(@ids) and p.{quality} is not null and p.""time"" >= limits.range_start and p.""time"" < limits.range_end
    union all
    select i.id, p.""time"", p.{column}, p.{quality} from unnest(@ids) i(id), limits cross join lateral (
        select * from {table} p
        where p.{_seriesColumn} = i.id and p.{quality} is not null and p.""time"" < limits.range_start
        order by p.""time"" desc limit 1
    ) p
),
segments as (
    select id, value, quality, greatest(""time"", limits.range_start) as from_time,
        coalesce(lead(""time"") over (partition by id order by ""time""), limits.range_end) as to_time
    from points, limits
),
held as (select * from segments where value is not null {bad})
select h.id, x.bucket, {calculation}
from held h cross join lateral (
    select b as bucket, extract(epoch from least(h.to_time, b + @interval) - greatest(h.from_time, b))::double precision as seconds
    from generate_series(time_bucket(@interval, h.from_time, origin => {_origin}), h.to_time, @interval) b
    where b < h.to_time and b + @interval > h.from_time
) x
where h.to_time > h.from_time
group by 1, 2;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("interval", query.Interval);
            command.Parameters.AddWithValue("ids", series.Keys.ToArray());
            command.Parameters.AddWithValue("start", query.Start.UtcDateTime);
            command.Parameters.AddWithValue("stop", query.Stop.UtcDateTime);
            if (function == TrakHoundTimeSeriesAggregate.DurationInState) command.Parameters.AddWithValue("state", TrakHoundTimeSeriesValidation.GetFieldValue(query.State, out _));

            var name = TrakHoundTimeSeriesAggregateQuery.GetColumn(field, function);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                GetAggregateRow(values, reader.GetInt64(0), reader.GetFieldValue<DateTime>(1))[name] = reader.GetDouble(2);
            }
        }

        private static Dictionary<string, object> GetAggregateRow(Dictionary<long, Dictionary<long, Dictionary<string, object>>> values, long seriesId, DateTime bucket)
        {
            if (!values.TryGetValue(seriesId, out var series))
            {
                series = new Dictionary<long, Dictionary<string, object>>();
                values[seriesId] = series;
            }

            if (!series.TryGetValue(bucket.Ticks, out var row))
            {
                row = new Dictionary<string, object>();
                series[bucket.Ticks] = row;
            }

            return row;
        }

        private static string GetAggregateSql(TrakHoundTimeSeriesAggregate function, string column, TrakHoundTimeSeriesFieldType type)
        {
            switch (function)
            {
                case TrakHoundTimeSeriesAggregate.Mean: return $"avg({column})::double precision";
                case TrakHoundTimeSeriesAggregate.Min: return $"min({column})";
                case TrakHoundTimeSeriesAggregate.Max: return $"max({column})";
                case TrakHoundTimeSeriesAggregate.Sum: return type == TrakHoundTimeSeriesFieldType.Long ? $"sum({column})::bigint" : $"sum({column})";
                case TrakHoundTimeSeriesAggregate.Count: return $"count({column})";
                case TrakHoundTimeSeriesAggregate.First: return $"first({column}, \"time\")";
                default: return $"last({column}, \"time\")";
            }
        }

        /// <summary>
        /// Reads the Rows of a Read, whose Query returns the Series ID, whether a Row is the Point before Start, the time, then each Field and its Quality.
        /// The Query returns up to one more Point than Take for each Series, which sets HasMore
        /// </summary>
        private async Task<TrakHoundTimeSeriesResult[]> ReadResults(NpgsqlCommand command, string measurement, Dictionary<long, Dictionary<string, string>> series, string[] names, bool onlyUsedFields, int take, CancellationToken cancellationToken)
        {
            var rows = new Dictionary<long, List<(bool Previous, object[] Values)>>();

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetInt64(0);
                    if (!rows.TryGetValue(id, out var seriesRows))
                    {
                        seriesRows = new List<(bool, object[])>();
                        rows[id] = seriesRows;
                    }

                    var row = new object[names.Length * 2 + 1];
                    row[0] = new DateTimeOffset(reader.GetFieldValue<DateTime>(2).Ticks, TimeSpan.Zero);
                    for (var i = 0; i < names.Length; i++)
                    {
                        var valueIndex = i * 2 + 3;
                        row[i * 2 + 1] = reader.IsDBNull(valueIndex) ? null : reader.GetValue(valueIndex);
                        row[i * 2 + 2] = reader.IsDBNull(valueIndex + 1) ? null : ((TrakHoundTimeSeriesQuality)reader.GetInt16(valueIndex + 1)).ToString();
                    }

                    seriesRows.Add((reader.GetInt32(1) == 1, row));
                }
            }

            var results = new List<TrakHoundTimeSeriesResult>();
            foreach (var item in series)
            {
                if (!rows.TryGetValue(item.Key, out var seriesRows)) continue;

                // The extra Point is the last one in the order of the Query, with the Point before Start ignored
                var hasMore = seriesRows.Count(o => !o.Previous) > take;
                if (hasMore) seriesRows.RemoveAt(seriesRows.FindLastIndex(o => !o.Previous));

                // Reading every Field only returns the ones this Series uses
                var used = Enumerable.Range(0, names.Length).Where(i => !onlyUsedFields || seriesRows.Any(row => row.Values[i * 2 + 2] != null)).ToArray();

                results.Add(new TrakHoundTimeSeriesResult
                {
                    Measurement = measurement,
                    Tags = new Dictionary<string, string>(item.Value),
                    Columns = TrakHoundTimeSeriesResult.GetColumns(used.Select(i => names[i])),
                    Rows = seriesRows.Select(row => new[] { row.Values[0] }.Concat(used.SelectMany(i => new[] { row.Values[i * 2 + 1], row.Values[i * 2 + 2] })).ToArray()).ToArray(),
                    HasMore = hasMore
                });
            }

            return results.ToArray();
        }

        /// <summary>
        /// Gets the Columns of a Read for each Field and its Quality. A Field the Measurement doesn't have, or that has never had a Value, returns null
        /// </summary>
        private static string GetFieldSelect(string[] names, IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?> columns)
        {
            return string.Join(", ", names.Select(name =>
            {
                if (!columns.TryGetValue(name, out var type)) return "null, null::smallint";
                return $"{(type != null ? $"p.{Quote(name)}" : "null")}, p.{QualityColumn(name)}";
            }));
        }

        private async Task<Dictionary<long, Dictionary<string, string>>> GetSeries(NpgsqlConnection connection, string measurement, IReadOnlyDictionary<string, string[]> tags, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand { Connection = connection };

            // A Tag with one Value can use the index on the Tags
            var single = tags?.Where(o => o.Value.Length == 1).ToDictionary(o => o.Key, o => o.Value[0]);
            var sql = new StringBuilder($"select id, tags from {SeriesTable} where measurement = @measurement and tags @> @tags");

            var index = 0;
            foreach (var tag in tags?.Where(o => o.Value.Length > 1) ?? Enumerable.Empty<KeyValuePair<string, string[]>>())
            {
                sql.Append($" and tags ->> @k{index} = any(@v{index})");
                command.Parameters.AddWithValue($"k{index}", tag.Key);
                command.Parameters.AddWithValue($"v{index}", tag.Value);
                index++;
            }

            sql.Append(" order by tags_key;");
            command.CommandText = sql.ToString();
            command.Parameters.AddWithValue("measurement", measurement);
            command.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb, ToJson(single));

            var output = new Dictionary<long, Dictionary<string, string>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                output[reader.GetInt64(0)] = FromJson(reader.GetString(1));
            }

            return output;
        }

        /// <summary>
        /// Gets the type of each Field of a Measurement (null if it has no Hypertable yet). Reads refresh these, since another Instance may have added a Field
        /// </summary>
        private async Task<IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?>> GetColumns(NpgsqlConnection connection, string measurement, bool refresh, CancellationToken cancellationToken)
        {
            if (!refresh && _columns.TryGetValue(measurement, out var cached)) return cached;

            var columns = await ReadColumns(connection, null, measurement, cancellationToken);

            if (columns != null) _columns[measurement] = columns;
            else _columns.TryRemove(measurement, out _);

            return columns;
        }

        private async Task<IReadOnlyDictionary<string, TrakHoundTimeSeriesFieldType?>> ReadColumns(NpgsqlConnection connection, NpgsqlTransaction transaction, string measurement, CancellationToken cancellationToken)
        {
            var sql = @"select a.attname, format_type(a.atttypid, null), a.attnum
from pg_attribute a
where a.attrelid = to_regclass(@table) and a.attnum > 0 and not a.attisdropped;";

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("table", Table(measurement));

            var output = new Dictionary<string, TrakHoundTimeSeriesFieldType?>();
            var found = false;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found = true;

                var name = reader.GetString(0);
                if (name == "time" || name == "$series") continue;

                // A Field that has never had a Value only has its Quality Column
                if (name.StartsWith(_qualityPrefix, StringComparison.Ordinal)) output.TryAdd(name.Substring(_qualityPrefix.Length), null);
                else output[name] = GetFieldType(reader.GetString(1));
            }

            return found ? output : null;
        }

        #endregion

        #region "Connections"

        private async Task<TrakHoundServiceResponse<TResult>> Run<TResult>(CancellationToken cancellationToken, Func<NpgsqlConnection, Task<TrakHoundServiceResponse<TResult>>> action)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(_connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await Initialize(connection, cancellationToken);

                    return await action(connection);
                }
                catch (PostgresException ex) when (attempt == 0 && IsStale(ex.SqlState))
                {
                    // Something dropped a Table or Column this Service had cached, so read them again
                    _columns.Clear();
                    _seriesIds.Clear();
                    _initialized = false;
                }
                catch (PostgresException ex)
                {
                    return new TrakHoundServiceResponse<TResult>(default, PostgresErrors.GetStatusCode(ex.SqlState), $"PostgresException : {ex.SqlState} : {ex.MessageText}");
                }
                catch (NpgsqlException ex)
                {
                    return new TrakHoundServiceResponse<TResult>(default, PostgresErrors.GetStatusCode(ex.SqlState), ex.Message);
                }
            }
        }

        private static bool IsStale(string sqlState) => sqlState == "42P01" || sqlState == "42703" || sqlState == "3F000"; // Undefined Table, Column or Schema

        private async Task Initialize(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            if (_initialized) return;

            await _initializeLock.WaitAsync(cancellationToken);
            try
            {
                if (_initialized) return;

                // Reads don't need permission to create anything once the Schema is there
                await using (var check = new NpgsqlCommand("select to_regclass(@table) is not null;", connection))
                {
                    check.Parameters.AddWithValue("table", SeriesTable);
                    if ((bool)await check.ExecuteScalarAsync(cancellationToken))
                    {
                        _initialized = true;
                        return;
                    }
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using (var command = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key, 0));", connection, transaction))
                {
                    command.Parameters.AddWithValue("key", $"trakhound-timeseries:{_schema}");
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                var sql = $@"create extension if not exists timescaledb;
create schema if not exists {_schema};
create table if not exists {SeriesTable} (id bigserial primary key, measurement text not null, tags_key text not null, tags jsonb not null, unique (measurement, tags_key));
create index if not exists {Quote("$series_tags")} on {SeriesTable} using gin (tags jsonb_path_ops);";

                await using (var command = new NpgsqlCommand(sql, connection, transaction))
                {
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                _initialized = true;
            }
            finally
            {
                _initializeLock.Release();
            }
        }

        #endregion

        #region "Names"

        private string SeriesTable => $"{_schema}.{Quote("$series")}";

        private string Table(string measurement) => $"{_schema}.{Quote(measurement)}";

        private static string QualityColumn(string field) => Quote($"{_qualityPrefix}{field}");

        private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

        private static string GetColumnType(TrakHoundTimeSeriesFieldType type)
        {
            switch (type)
            {
                case TrakHoundTimeSeriesFieldType.Double: return "double precision";
                case TrakHoundTimeSeriesFieldType.Long: return "bigint";
                case TrakHoundTimeSeriesFieldType.Bool: return "boolean";
                default: return "text";
            }
        }

        private static TrakHoundTimeSeriesFieldType GetFieldType(string columnType)
        {
            switch (columnType)
            {
                case "double precision": return TrakHoundTimeSeriesFieldType.Double;
                case "bigint": return TrakHoundTimeSeriesFieldType.Long;
                case "boolean": return TrakHoundTimeSeriesFieldType.Bool;
                default: return TrakHoundTimeSeriesFieldType.String;
            }
        }

        private static string GetTagsKey(IReadOnlyDictionary<string, string> tags)
        {
            return JsonSerializer.Serialize(tags == null ? new SortedDictionary<string, string>(StringComparer.Ordinal) : new SortedDictionary<string, string>(tags.ToDictionary(o => o.Key, o => o.Value), StringComparer.Ordinal));
        }

        private static string ToJson(IReadOnlyDictionary<string, string> tags) => GetTagsKey(tags);

        private static Dictionary<string, string> FromJson(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

        #endregion
    }
}
