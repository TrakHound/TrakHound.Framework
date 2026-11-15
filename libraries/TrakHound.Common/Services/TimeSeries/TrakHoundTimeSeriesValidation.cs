// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// The rules every Time Series Service applies to Requests. Each method returns a message for a Bad Request, or null if it's valid
    /// </summary>
    public static class TrakHoundTimeSeriesValidation
    {
        // Services build SQL identifiers from these names, and PostgreSQL truncates identifiers past 63 bytes
        private static readonly Regex _name = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.Compiled);

        // Services store a Field's Quality in a Column named from the Field with a prefix of up to 3 characters
        private static readonly Regex _fieldName = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,59}$", RegexOptions.Compiled);

        // These are Result Columns or are reserved by a database (InfluxDB's "time")
        private static readonly HashSet<string> _reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            TrakHoundTimeSeriesResult.TimestampColumn,
            "quality",
            "time"
        };


        public static string ValidateMeasurement(string measurement)
        {
            return IsName(measurement) ? null : $"The Measurement \"{measurement}\" is not valid. Names start with a letter or underscore, and have up to 63 letters, numbers and underscores";
        }

        public static string ValidateTags(IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null) return null;

            foreach (var tag in tags)
            {
                if (!IsName(tag.Key) || _reserved.Contains(tag.Key)) return $"The Tag \"{tag.Key}\" is not valid";
                if (string.IsNullOrEmpty(tag.Value)) return $"The Tag \"{tag.Key}\" needs a Value";
            }

            return null;
        }

        /// <summary>
        /// Checks the Tags of a Query, which can each have several Values
        /// </summary>
        public static string ValidateTagFilter(IReadOnlyDictionary<string, string[]> tags)
        {
            if (tags == null) return null;

            foreach (var tag in tags)
            {
                if (!IsName(tag.Key) || _reserved.Contains(tag.Key)) return $"The Tag \"{tag.Key}\" is not valid";
                if (tag.Value == null || tag.Value.Length < 1 || tag.Value.Any(string.IsNullOrEmpty)) return $"The Tag \"{tag.Key}\" needs a Value";
            }

            return null;
        }

        public static string ValidateFields(IEnumerable<string> fields)
        {
            if (fields == null) return null;

            foreach (var field in fields)
            {
                if (field == null || !_fieldName.IsMatch(field) || _reserved.Contains(field)) return $"The Field \"{field}\" is not valid. Names start with a letter or underscore, and have up to 60 letters, numbers and underscores";
            }

            return null;
        }

        public static string ValidateRange(DateTimeOffset start, DateTimeOffset stop)
        {
            return start < stop ? null : "Start must be before Stop";
        }

        public static string ValidatePoint(TrakHoundTimeSeriesPoint point)
        {
            if (point == null) return "A Point is required";

            var message = ValidateMeasurement(point.Measurement) ?? ValidateTags(point.Tags);
            if (message != null) return message;

            if (point.Timestamp == default) return "A Point needs a Timestamp";
            if (point.Fields == null || point.Fields.Count < 1) return "A Point needs a Field";

            message = ValidateFields(point.Fields.Keys);
            if (message != null) return message;

            foreach (var field in point.Fields)
            {
                if (IsNoValue(field.Value))
                {
                    if (point.GetQuality(field.Key) != TrakHoundTimeSeriesQuality.Bad) return $"The Field \"{field.Key}\" has no Value, so its Quality must be Bad";
                }
                else if (GetFieldValue(field.Value, out _) == null)
                {
                    return $"The Value of the Field \"{field.Key}\" must be a finite number, a bool or a string";
                }
            }

            if (point.Qualities != null)
            {
                foreach (var field in point.Qualities.Keys)
                {
                    if (!point.Fields.ContainsKey(field)) return $"The Point has a Quality for the Field \"{field}\", but not the Field";
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether a Value is null, which a Bad Field uses to record that it has no Value
        /// </summary>
        public static bool IsNoValue(object value) => value == null || (value is JsonElement element && element.ValueKind == JsonValueKind.Null);

        public static string ValidateQuery(TrakHoundTimeSeriesQuery query)
        {
            if (query == null) return "A Query is required";

            var message = ValidateMeasurement(query.Measurement) ?? ValidateTagFilter(query.Tags) ?? ValidateFields(query.Fields) ?? ValidateRange(query.Start, query.Stop);
            if (message != null) return message;

            return query.Take < 1 ? "Take must be at least 1" : null;
        }

        public static string ValidateQuery(TrakHoundTimeSeriesAggregateQuery query)
        {
            var message = ValidateQuery((TrakHoundTimeSeriesQuery)query);
            if (message != null) return message;

            if (query.Fields == null || query.Fields.Length < 1) return "An Aggregate needs a Field";
            if (query.Functions == null || query.Functions.Length < 1) return "An Aggregate needs a Function";
            if (query.Interval <= TimeSpan.Zero) return "The Interval must be greater than zero";

            if (query.Functions.Contains(TrakHoundTimeSeriesAggregate.DurationInState) && GetFieldValue(query.State, out _) == null)
            {
                return "DurationInState needs a State that is a finite number, a bool or a string";
            }

            return null;
        }

        /// <summary>
        /// Checks that the Functions can run on the Fields. getType returns the type of a Field, or null if the Measurement doesn't have it
        /// </summary>
        public static string ValidateTypes(TrakHoundTimeSeriesAggregateQuery query, Func<string, TrakHoundTimeSeriesFieldType?> getType)
        {
            foreach (var field in query.Fields)
            {
                var type = getType(field);
                if (type == null) continue;

                foreach (var function in query.Functions)
                {
                    if (!IsSupported(function, type.Value)) return $"{function} can't run on the Field \"{field}\", which is a {type}";
                }

                if (query.Functions.Contains(TrakHoundTimeSeriesAggregate.DurationInState))
                {
                    var state = GetFieldValue(query.State, out var stateType);
                    if (stateType != type && ConvertFieldValue(state, type.Value) == null) return $"The State must have the same type as the Field \"{field}\", which is a {type}";
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the type each Field is written as (Measurement and Field => type), or a message if a Value can't be written. getType returns the type a Field already has, or null.
        /// A whole number can be written to a double Field, since JSON clients can't always send 8000.0 (JavaScript sends 8000). A new Field written with both is a double.
        /// A new Field that only has null Values isn't included, since it doesn't have a type until its first Value
        /// </summary>
        public static string GetWriteTypes(IEnumerable<TrakHoundTimeSeriesPoint> points, Func<string, string, TrakHoundTimeSeriesFieldType?> getType, out Dictionary<(string Measurement, string Field), TrakHoundTimeSeriesFieldType> types)
        {
            types = new Dictionary<(string, string), TrakHoundTimeSeriesFieldType>();
            var existing = new HashSet<(string, string)>();

            foreach (var point in points)
            {
                foreach (var field in point.Fields)
                {
                    var key = (point.Measurement, field.Key);

                    if (IsNoValue(field.Value))
                    {
                        if (types.ContainsKey(key)) continue;

                        var storedType = getType(point.Measurement, field.Key);
                        if (storedType != null)
                        {
                            existing.Add(key);
                            types[key] = storedType.Value;
                        }

                        continue;
                    }

                    GetFieldValue(field.Value, out var type);

                    if (types.TryGetValue(key, out var current))
                    {
                        // A Field that already exists keeps its type, which the second pass checks
                        if (!existing.Contains(key) && current != type)
                        {
                            if (!IsNumber(current) || !IsNumber(type)) return GetTypeMessage(field.Key, point.Measurement, current);
                            types[key] = TrakHoundTimeSeriesFieldType.Double;
                        }
                    }
                    else
                    {
                        var stored = getType(point.Measurement, field.Key);
                        if (stored != null) existing.Add(key);
                        types[key] = stored ?? type;
                    }
                }
            }

            // Checked once every Value has been seen, since a later double makes a new Field a double
            foreach (var point in points)
            {
                foreach (var field in point.Fields)
                {
                    if (IsNoValue(field.Value)) continue;

                    var target = types[(point.Measurement, field.Key)];
                    if (ConvertFieldValue(GetFieldValue(field.Value, out _), target) == null) return GetTypeMessage(field.Key, point.Measurement, target);
                }
            }

            return null;
        }

        /// <summary>
        /// Converts a stored type of Value (from GetFieldValue) to the type of a Field. Returns null if it can't be, including a long too large for a double to hold exactly
        /// </summary>
        public static object ConvertFieldValue(object value, TrakHoundTimeSeriesFieldType type)
        {
            if (value is long number && type == TrakHoundTimeSeriesFieldType.Double)
            {
                var converted = (double)number;

                // long.MaxValue rounds up to 2^63, which doesn't fit back into a long, so the range is checked before casting
                return converted < 9223372036854775808d && (long)converted == number ? converted : null;
            }

            GetFieldValue(value, out var valueType);
            return value != null && valueType == type ? value : null;
        }

        /// <summary>
        /// Gets the Take a Read uses
        /// </summary>
        public static int GetTake(int? take) => Math.Min(take ?? TrakHoundTimeSeriesQuery.DefaultTake, TrakHoundTimeSeriesQuery.MaxTake);

        /// <summary>
        /// Returns whether a Function can run on a type of Field
        /// </summary>
        public static bool IsSupported(TrakHoundTimeSeriesAggregate function, TrakHoundTimeSeriesFieldType type)
        {
            switch (function)
            {
                case TrakHoundTimeSeriesAggregate.Mean:
                case TrakHoundTimeSeriesAggregate.Min:
                case TrakHoundTimeSeriesAggregate.Max:
                case TrakHoundTimeSeriesAggregate.Sum:
                case TrakHoundTimeSeriesAggregate.TimeWeightedMean:
                    return type == TrakHoundTimeSeriesFieldType.Double || type == TrakHoundTimeSeriesFieldType.Long;
            }

            return true;
        }

        /// <summary>
        /// Converts a Value to the type it's stored as (a double, long, bool or string). Returns null if it isn't one of the supported Values
        /// </summary>
        public static object GetFieldValue(object value, out TrakHoundTimeSeriesFieldType type)
        {
            type = default;

            switch (value)
            {
                // A value a client sent as JSON (a number without a decimal point is a whole number)
                case JsonElement element:
                    var read = TrakHoundTimeSeriesJson.Read(element);
                    return read is JsonElement ? null : GetFieldValue(read, out type);

                case double or float or decimal:
                    var number = Convert.ToDouble(value);
                    if (double.IsNaN(number) || double.IsInfinity(number)) return null; // InfluxDB can't store these
                    type = TrakHoundTimeSeriesFieldType.Double;
                    return number;

                case byte or sbyte or short or ushort or int or uint or long:
                    type = TrakHoundTimeSeriesFieldType.Long;
                    return Convert.ToInt64(value);

                case bool:
                    type = TrakHoundTimeSeriesFieldType.Bool;
                    return value;

                case string:
                    type = TrakHoundTimeSeriesFieldType.String;
                    return value;
            }

            return null;
        }


        private static bool IsNumber(TrakHoundTimeSeriesFieldType type) => type == TrakHoundTimeSeriesFieldType.Double || type == TrakHoundTimeSeriesFieldType.Long;

        private static string GetTypeMessage(string field, string measurement, TrakHoundTimeSeriesFieldType type) => $"The Field \"{field}\" of \"{measurement}\" is a {type}, so this Value can't be written to it";

        private static bool IsName(string name) => name != null && _name.IsMatch(name);
    }
}
