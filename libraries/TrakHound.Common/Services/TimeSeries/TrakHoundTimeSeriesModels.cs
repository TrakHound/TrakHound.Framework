// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// Whether a Value can be trusted (ex. Bad when a sensor has failed)
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTimeSeriesQuality>))]
    public enum TrakHoundTimeSeriesQuality
    {
        Good,
        Uncertain,
        Bad
    }

    /// <summary>
    /// A calculation for each Interval of an Aggregate Query
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTimeSeriesAggregate>))]
    public enum TrakHoundTimeSeriesAggregate
    {
        Mean,
        Min,
        Max,
        Sum,
        Count,
        First,
        Last,
        /// <summary>
        /// The mean with each Value weighted by how long it held
        /// </summary>
        TimeWeightedMean,
        /// <summary>
        /// The seconds the Field held the State of the Query
        /// </summary>
        DurationInState
    }

    /// <summary>
    /// What an Aggregate Query returns for an Interval without a value
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTimeSeriesFill>))]
    public enum TrakHoundTimeSeriesFill
    {
        /// <summary>
        /// Leaves out an Interval that has no values
        /// </summary>
        None,
        /// <summary>
        /// Returns null
        /// </summary>
        Null,
        /// <summary>
        /// Returns the value of the previous Interval
        /// </summary>
        Previous
    }

    /// <summary>
    /// The type a Field keeps within a Measurement
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTimeSeriesFieldType>))]
    public enum TrakHoundTimeSeriesFieldType
    {
        Double,
        Long,
        Bool,
        String
    }


    /// <summary>
    /// Values recorded at one time for a Series
    /// </summary>
    public class TrakHoundTimeSeriesPoint
    {
        /// <summary>
        /// What is being recorded (ex. "spindle")
        /// </summary>
        [JsonPropertyName("measurement")]
        public string Measurement { get; set; }

        /// <summary>
        /// What identifies the Series (ex. machine=M1). Use a small set of known values, since each new combination creates a Series
        /// </summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }

        /// <summary>
        /// When the Values were recorded
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// The Values (a double, long, bool or string). A Bad Field can have a null Value, which records that it has no Value at this time
        /// </summary>
        [JsonPropertyName("fields")]
        [JsonConverter(typeof(TrakHoundTimeSeriesFieldsJsonConverter))]
        public Dictionary<string, object> Fields { get; set; }

        /// <summary>
        /// Whether the Values can be trusted. This applies to each Field that isn't in Qualities
        /// </summary>
        [JsonPropertyName("quality")]
        public TrakHoundTimeSeriesQuality Quality { get; set; }

        /// <summary>
        /// The Quality of the Fields that differ from Quality (Field => Quality)
        /// </summary>
        [JsonPropertyName("qualities")]
        public Dictionary<string, TrakHoundTimeSeriesQuality> Qualities { get; set; }


        /// <summary>
        /// Gets the Quality of a Field
        /// </summary>
        public TrakHoundTimeSeriesQuality GetQuality(string field) => Qualities != null && Qualities.TryGetValue(field, out var quality) ? quality : Quality;
    }

    /// <summary>
    /// A unique combination of Tags within a Measurement
    /// </summary>
    public class TrakHoundTimeSeries
    {
        [JsonPropertyName("measurement")]
        public string Measurement { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }
    }

    /// <summary>
    /// A Field of a Measurement
    /// </summary>
    public class TrakHoundTimeSeriesField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The type of the Field's Values (null until the Field has a Value)
        /// </summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundTimeSeriesFieldType? Type { get; set; }
    }

    /// <summary>
    /// Selects the Points of a Measurement within a time range
    /// </summary>
    public class TrakHoundTimeSeriesQuery
    {
        public const int DefaultTake = 1000;
        public const int MaxTake = 10000;


        [JsonPropertyName("measurement")]
        public string Measurement { get; set; }

        /// <summary>
        /// Selects the Series that have one of the Values of each of these Tags, so one Query can read many Series (null selects every Series).
        /// In JSON a Tag can be one string or an array of strings
        /// </summary>
        [JsonPropertyName("tags")]
        [JsonConverter(typeof(TrakHoundTimeSeriesTagFilterJsonConverter))]
        public Dictionary<string, string[]> Tags { get; set; }

        /// <summary>
        /// The Fields to return (null returns every Field)
        /// </summary>
        [JsonPropertyName("fields")]
        public string[] Fields { get; set; }

        /// <summary>
        /// The start of the range (inclusive)
        /// </summary>
        [JsonPropertyName("start")]
        public DateTimeOffset Start { get; set; }

        /// <summary>
        /// The end of the range (exclusive)
        /// </summary>
        [JsonPropertyName("stop")]
        public DateTimeOffset Stop { get; set; }

        /// <summary>
        /// Read : The most Points to return for each Series (null uses DefaultTake, and anything above MaxTake uses MaxTake). To page, use the last Timestamp as the next Start
        /// </summary>
        [JsonPropertyName("take")]
        public int? Take { get; set; }

        /// <summary>
        /// Read : True returns the newest Points first
        /// </summary>
        [JsonPropertyName("descending")]
        public bool Descending { get; set; }

        /// <summary>
        /// Read : True also returns the last Point before Start for each Series, so state calculations have a starting Value. It doesn't count toward Take
        /// </summary>
        [JsonPropertyName("includePrevious")]
        public bool IncludePrevious { get; set; }


        /// <summary>
        /// Gets the Tags of a Query that selects one Series (or the Series that have all of these Tags)
        /// </summary>
        public static Dictionary<string, string[]> GetTagFilter(IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null) return null;

            var output = new Dictionary<string, string[]>();
            foreach (var tag in tags) output[tag.Key] = new[] { tag.Value };
            return output;
        }
    }

    /// <summary>
    /// Calculates values for each Interval of a time range. Each Series has its own Result
    /// </summary>
    public class TrakHoundTimeSeriesAggregateQuery : TrakHoundTimeSeriesQuery
    {
        /// <summary>
        /// The length of each Interval. Intervals are aligned to the Unix epoch, and a Row's Timestamp is the start of its Interval
        /// </summary>
        [JsonPropertyName("interval")]
        public TimeSpan Interval { get; set; }

        /// <summary>
        /// The calculations. Each makes a Column for each Field, named by GetColumn
        /// </summary>
        [JsonPropertyName("functions")]
        public TrakHoundTimeSeriesAggregate[] Functions { get; set; }

        /// <summary>
        /// What to return for an Interval without a value. Count is 0 instead of being filled
        /// </summary>
        [JsonPropertyName("fill")]
        public TrakHoundTimeSeriesFill Fill { get; set; }

        /// <summary>
        /// DurationInState : The Value to measure
        /// </summary>
        [JsonPropertyName("state")]
        [JsonConverter(typeof(TrakHoundTimeSeriesValueJsonConverter))]
        public object State { get; set; }

        /// <summary>
        /// True includes Bad Points. Otherwise they are skipped, and the time after one until the next Point isn't counted by the time weighted Functions
        /// </summary>
        [JsonPropertyName("includeBadQuality")]
        public bool IncludeBadQuality { get; set; }


        /// <summary>
        /// Gets the name of a Result Column (ex. "speed_mean")
        /// </summary>
        public static string GetColumn(string field, TrakHoundTimeSeriesAggregate function) => $"{field}_{function.ToString().ToLowerInvariant()}";
    }

    /// <summary>
    /// The newest Value of one Field. A Value can be null if the Field is Bad
    /// </summary>
    public class TrakHoundTimeSeriesLatestValue
    {
        [JsonPropertyName("field")]
        public string Field { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("value")]
        [JsonConverter(typeof(TrakHoundTimeSeriesValueJsonConverter))]
        public object Value { get; set; }

        [JsonPropertyName("quality")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundTimeSeriesQuality Quality { get; set; }
    }

    /// <summary>
    /// The newest Value of each Field of one Series. Fields can have different Timestamps, since a Point doesn't have to include every Field
    /// </summary>
    public class TrakHoundTimeSeriesLatestResult
    {
        [JsonPropertyName("measurement")]
        public string Measurement { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }

        [JsonPropertyName("values")]
        public TrakHoundTimeSeriesLatestValue[] Values { get; set; }
    }

    /// <summary>
    /// The Rows of one Series. The first Column is the Timestamp. Read then has a Column for each Field and one for its Quality, and Aggregate has a Column for each
    /// calculated value (null when a Row has none). A Field's Quality is null when a Point doesn't have the Field, so a Field with a null Value and a Quality has no Value at that time
    /// </summary>
    public class TrakHoundTimeSeriesResult : IJsonOnDeserialized
    {
        public const string TimestampColumn = "timestamp";


        [JsonPropertyName("measurement")]
        public string Measurement { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }

        [JsonPropertyName("columns")]
        public string[] Columns { get; set; }

        [JsonPropertyName("rows")]
        [JsonConverter(typeof(TrakHoundTimeSeriesRowsJsonConverter))]
        public object[][] Rows { get; set; }

        /// <summary>
        /// Read : True if Take left out Points of this Series. To page, use the last Timestamp as the next Start
        /// </summary>
        [JsonPropertyName("hasMore")]
        public bool HasMore { get; set; }


        /// <summary>
        /// Gets the name of the Column with a Field's Quality (ex. "speed.quality"). A Field name can't contain a period, so this can't match a Field
        /// </summary>
        public static string GetQualityColumn(string field) => $"{field}.quality";

        /// <summary>
        /// Returns whether a Column has a Field's Quality
        /// </summary>
        public static bool IsQualityColumn(string column) => column != null && column.EndsWith(".quality", StringComparison.Ordinal);

        /// <summary>
        /// Gets the Columns of a Read: the Timestamp, then each Field and its Quality
        /// </summary>
        public static string[] GetColumns(IEnumerable<string> fields)
        {
            var columns = new List<string> { TimestampColumn };
            foreach (var field in fields)
            {
                columns.Add(field);
                columns.Add(GetQualityColumn(field));
            }

            return columns.ToArray();
        }


        void IJsonOnDeserialized.OnDeserialized()
        {
            // JSON has no Timestamp type, and only the first Column is known to be one
            if (Rows == null) return;

            foreach (var row in Rows)
            {
                if (row.Length > 0 && row[0] is string timestamp && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) row[0] = parsed.ToUniversalTime();
            }
        }
    }
}
