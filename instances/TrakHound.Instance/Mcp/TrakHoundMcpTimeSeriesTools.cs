// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using TrakHound.Instances;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// Time Series Service Tools. Service operations are authorized by the Service (see TrakHoundServiceSecurity), so these Tools don't declare
    /// a System Resource. There's no Delete Tool, since a client shouldn't be able to erase history, and not every database can delete
    /// </summary>
    [McpServerToolType]
    public class TrakHoundMcpTimeSeriesTools : TrakHoundMcpTools
    {
        public const int MaxRows = 1000;
        public const int MaxSeries = 100;
        private const string RouterDescription = "The Service Router (leave empty for the default)";
        private const string TagsDescription = "Selects the Series that have all of these Tags (ex. {\"machine\":\"M1\"}). Leave empty for every Series";
        private const string TagFilterDescription = "Selects the Series that have one of the values of each of these Tags, so one call can read many Series (ex. {\"machine\":[\"M1\",\"M2\"]}). Leave empty for every Series";
        private const string TimeDescription = "An ISO 8601 time. UTC is used if it has no offset";


        public TrakHoundMcpTimeSeriesTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "timeseries_measurements", Title = "List Measurements", ReadOnly = true)]
        [Description("Lists the Measurements of the Time Series Service.")]
        [TrakHoundMcpTool]
        public async Task<string> Measurements([Description(RouterDescription)] string routerId = null, CancellationToken cancellationToken = default)
        {
            return ToJson(Check(await GetClient().Services.TimeSeries.ListMeasurements(Router(routerId), cancellationToken), "List Measurements"));
        }

        [McpServerTool(Name = "timeseries_series", Title = "List Series", ReadOnly = true)]
        [Description("Lists the Series (unique combinations of Tags) of a Measurement.")]
        [TrakHoundMcpTool]
        public async Task<string> Series(
            [Description("The Measurement (from timeseries_measurements)")] string measurement,
            [Description(TagsDescription)] Dictionary<string, string> tags = null,
            [Description("Series to skip (for paging)")] int skip = 0,
            [Description("The most Series to return (up to 1000)")] int take = 100,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var series = Check(await GetClient().Services.TimeSeries.ListSeries(measurement, tags, Math.Max(0, skip), Clamp(take, 1, MaxRows), Router(routerId), cancellationToken), "List Series");
            return ToJson(series?.Select(o => o.Tags));
        }

        [McpServerTool(Name = "timeseries_fields", Title = "List Fields", ReadOnly = true)]
        [Description("Lists the Fields of a Measurement and their types (Double, Long, Bool or String). Use it before timeseries_aggregate, since Mean, Min, Max, Sum and TimeWeightedMean need a number.")]
        [TrakHoundMcpTool]
        public async Task<string> Fields(
            [Description("The Measurement (from timeseries_measurements)")] string measurement,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var fields = Check(await GetClient().Services.TimeSeries.ListFields(measurement, Router(routerId), cancellationToken), "List Fields");
            return ToJson(fields?.Select(o => new { name = o.Name, type = o.Type?.ToString() }));
        }

        [McpServerTool(Name = "timeseries_read", Title = "Read Points", ReadOnly = true)]
        [Description("Reads the Points of a Measurement from start (inclusive) to stop (exclusive), with a result for each Series. The first column is the timestamp (UTC), then each Field and its quality (ex. speed.quality: Good, Uncertain or Bad). A Field with a null value and a quality has no value at that time. If hasMore is true, use the last timestamp as the next start to read the rest. For long ranges use timeseries_aggregate.")]
        [TrakHoundMcpTool]
        public async Task<string> Read(
            [Description("The Measurement (from timeseries_measurements)")] string measurement,
            [Description("The start of the range. " + TimeDescription)] string start,
            [Description("The end of the range. " + TimeDescription)] string stop,
            [Description("The Fields to return (leave empty for every Field)")] string[] fields = null,
            [Description(TagFilterDescription)] Dictionary<string, string[]> tags = null,
            [Description("The most Points to return for each Series (up to 1000)")] int take = 200,
            [Description("Return the newest Points first")] bool descending = false,
            [Description("Also return the last Point before start, as the value at the start of the range")] bool includePrevious = false,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var query = new TrakHoundTimeSeriesQuery
            {
                Measurement = measurement,
                Tags = tags,
                Fields = EmptyToNull(fields),
                Start = ParseTime(start, nameof(start)),
                Stop = ParseTime(stop, nameof(stop)),
                Take = Clamp(take, 1, MaxRows),
                Descending = descending,
                IncludePrevious = includePrevious
            };

            return ToResultJson(Check(await GetClient().Services.TimeSeries.Read(query, Router(routerId), cancellationToken), "Read"));
        }

        [McpServerTool(Name = "timeseries_latest", Title = "Read Latest Values", ReadOnly = true)]
        [Description("Reads the newest value of each Field of each Series of a Measurement, with its own timestamp (UTC) and quality. A null value with Bad quality means the Field has no value.")]
        [TrakHoundMcpTool]
        public async Task<string> Latest(
            [Description("The Measurement (from timeseries_measurements)")] string measurement,
            [Description("The Fields to return (leave empty for every Field)")] string[] fields = null,
            [Description(TagFilterDescription)] Dictionary<string, string[]> tags = null,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var results = Check(await GetClient().Services.TimeSeries.ReadLatest(measurement, tags, EmptyToNull(fields), Router(routerId), cancellationToken), "Read Latest");
            return ToJson(new
            {
                seriesCount = results?.Length ?? 0,
                seriesTruncated = results?.Length > MaxSeries,
                results = results?.Take(MaxSeries).Select(o => new { tags = o.Tags, values = o.Values })
            });
        }

        [McpServerTool(Name = "timeseries_aggregate", Title = "Aggregate Points", ReadOnly = true)]
        [Description("Calculates functions for each interval from start to stop, with a result for each Series and a column for each Field and function (ex. speed_mean). Functions: Mean, Min, Max, Sum, Count, First, Last, TimeWeightedMean, and DurationInState (the seconds a Field held state). Use TimeWeightedMean and DurationInState for values that hold until they change, such as a machine state. Bad quality values are skipped unless includeBadQuality is set.")]
        [TrakHoundMcpTool]
        public async Task<string> Aggregate(
            [Description("The Measurement (from timeseries_measurements)")] string measurement,
            [Description("The Fields to calculate")] string[] fields,
            [Description("The start of the range. " + TimeDescription)] string start,
            [Description("The end of the range. " + TimeDescription)] string stop,
            [Description("The length of each interval, in seconds")] double intervalSeconds,
            [Description("The functions to calculate")] string[] functions,
            [Description("DurationInState: the value to measure, with the Field's type")] JsonElement? state = null,
            [Description(TagFilterDescription)] Dictionary<string, string[]> tags = null,
            [Description("What an interval without a value returns: None leaves it out, Null returns null, Previous returns the last value")] string fill = "None",
            [Description("Include Bad quality values")] bool includeBadQuality = false,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var query = new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = measurement,
                Tags = tags,
                Fields = fields,
                Start = ParseTime(start, nameof(start)),
                Stop = ParseTime(stop, nameof(stop)),
                Interval = GetInterval(intervalSeconds),
                Functions = ParseEnums<TrakHoundTimeSeriesAggregate>(functions, "function"),
                Fill = ParseEnums<TrakHoundTimeSeriesFill>(new[] { fill ?? "None" }, "fill")[0],
                State = state,
                IncludeBadQuality = includeBadQuality
            };

            // Asked for before running it, since the Service would calculate every interval
            var error = GetRangeError(query.Start, query.Stop, query.Interval);
            if (error != null) throw new McpException(error);

            return ToResultJson(Check(await GetClient().Services.TimeSeries.Aggregate(query, Router(routerId), cancellationToken), "Aggregate"));
        }

        [McpServerTool(Name = "timeseries_write", Title = "Write Point", Destructive = true, Idempotent = true)]
        [Description("Writes one Point. A Point with the same Measurement, Tags and timestamp replaces the Fields written here and their quality, so existing data can be changed. A new Field keeps the type of its first value (a number without a decimal point is a Long), and a Tag creates a new Series for each new value, so don't use unbounded values as Tags.")]
        [TrakHoundMcpTool(Write = true)]
        public async Task<string> Write(
            [Description("The Measurement")] string measurement,
            [Description("The Fields and their values: numbers, booleans or strings (ex. {\"speed\":8000.5,\"state\":\"RUN\"}). Use null with Bad quality for a Field that has no value")] Dictionary<string, JsonElement> fields,
            [Description("The Tags that identify the Series (ex. {\"machine\":\"M1\"})")] Dictionary<string, string> tags = null,
            [Description("The time of the Point (leave empty for the current time). " + TimeDescription)] string timestamp = null,
            [Description("The quality of the Fields: Good, Uncertain or Bad")] string quality = "Good",
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var point = new TrakHoundTimeSeriesPoint
            {
                Measurement = measurement,
                Tags = tags,
                Fields = fields?.ToDictionary(o => o.Key, o => (object)o.Value),
                Timestamp = string.IsNullOrWhiteSpace(timestamp) ? DateTimeOffset.UtcNow : ParseTime(timestamp, nameof(timestamp)),
                Quality = ParseEnums<TrakHoundTimeSeriesQuality>(new[] { quality ?? "Good" }, "quality")[0]
            };

            Check(await GetClient().Services.TimeSeries.Write(new[] { point }, Router(routerId), cancellationToken), "Write");
            return ToJson(new { success = true, timestamp = point.Timestamp.UtcDateTime });
        }


        /// <summary>
        /// Returns a message if a range would make more than MaxRows intervals
        /// </summary>
        public static string GetRangeError(DateTimeOffset start, DateTimeOffset stop, TimeSpan interval)
        {
            if (stop <= start) return "start must be before stop";

            var intervals = (stop - start).Ticks / interval.Ticks;
            if (intervals <= MaxRows) return null;

            var seconds = Math.Ceiling((stop - start).TotalSeconds / MaxRows);
            return $"This range makes {intervals:N0} intervals, and the limit is {MaxRows:N0}. Use an intervalSeconds of at least {seconds:0} or a shorter range";
        }

        /// <summary>
        /// Limits what's returned, and says when something was left out so a client doesn't take it as everything
        /// </summary>
        public static object GetOutput(TrakHoundTimeSeriesResult[] results)
        {
            if (results == null) return new { seriesCount = 0, results = Array.Empty<object>() };

            return new
            {
                seriesCount = results.Length,
                seriesTruncated = results.Length > MaxSeries,
                results = results.Take(MaxSeries).Select(o => new
                {
                    tags = o.Tags,
                    columns = o.Columns,
                    rowCount = o.Rows?.Length ?? 0,
                    truncated = (o.Rows?.Length ?? 0) > MaxRows,
                    hasMore = o.HasMore,
                    rows = o.Rows?.Take(MaxRows)
                }).ToList()
            };
        }

        private string ToResultJson(TrakHoundTimeSeriesResult[] results) => ToJson(GetOutput(results));

        private static TimeSpan GetInterval(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds / 2) throw new McpException("intervalSeconds must be greater than zero");
            return TimeSpan.FromSeconds(seconds);
        }

        private static DateTimeOffset ParseTime(string value, string name)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) return time;
            throw new McpException($"{name} must be an ISO 8601 time (ex. 2026-01-01T00:00:00Z)");
        }

        private static TEnum[] ParseEnums<TEnum>(string[] values, string name) where TEnum : struct, Enum
        {
            if (values == null || values.Length < 1) throw new McpException($"At least one {name} is required");

            var output = new TEnum[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                // A number would parse as any value of the enum, so only names are accepted
                if (!Enum.GetNames<TEnum>().Any(o => o.Equals(values[i], StringComparison.OrdinalIgnoreCase)) || !Enum.TryParse(values[i], true, out output[i]))
                {
                    throw new McpException($"\"{values[i]}\" isn't a {name}. Use {string.Join(", ", Enum.GetNames<TEnum>())}");
                }
            }

            return output;
        }

        private static string[] EmptyToNull(string[] values) => values == null || values.Length < 1 ? null : values;

        private static string Router(string routerId) => string.IsNullOrWhiteSpace(routerId) ? null : routerId;
    }
}
