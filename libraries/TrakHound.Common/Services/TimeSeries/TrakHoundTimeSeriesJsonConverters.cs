// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// Keeps the type of a Value in JSON. A Field keeps one type, but 8000.0 would normally be written as 8000 and read back as a long, so a double
    /// always has a decimal point or exponent, and a number without one is read as a long
    /// </summary>
    internal static class TrakHoundTimeSeriesJson
    {
        public static void Write(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case bool boolean: writer.WriteBooleanValue(boolean); break;
                case string text: writer.WriteStringValue(text); break;
                case DateTimeOffset timestamp: writer.WriteStringValue(timestamp); break;
                case byte or sbyte or short or ushort or int or uint or long: writer.WriteNumberValue(Convert.ToInt64(value)); break;
                case double or float or decimal: WriteDouble(writer, Convert.ToDouble(value)); break;
                default: JsonSerializer.Serialize(writer, value, value.GetType()); break;
            }
        }

        public static object Read(ref Utf8JsonReader reader)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Read(document.RootElement);
        }

        public static object Read(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    var text = element.GetRawText();
                    if (text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && element.TryGetInt64(out var integer)) return integer;
                    return element.GetDouble();
            }

            // An object or array isn't a Value, so validation rejects it
            return element.Clone();
        }

        private static void WriteDouble(Utf8JsonWriter writer, double value)
        {
            // Written as text would be invalid JSON. Validation rejects these before a Value is stored
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                writer.WriteNullValue();
                return;
            }

            var text = value.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOfAny(new[] { '.', 'E' }) < 0) text += ".0";
            writer.WriteRawValue(text, true);
        }
    }

    /// <summary>
    /// One Value (ex. the State of a Query)
    /// </summary>
    public class TrakHoundTimeSeriesValueJsonConverter : JsonConverter<object>
    {
        /// <inheritdoc/>
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => TrakHoundTimeSeriesJson.Read(ref reader);

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => TrakHoundTimeSeriesJson.Write(writer, value);
    }

    /// <summary>
    /// The Fields of a Point
    /// </summary>
    public class TrakHoundTimeSeriesFieldsJsonConverter : JsonConverter<Dictionary<string, object>>
    {
        /// <inheritdoc/>
        public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Fields must be an object");

            var fields = new Dictionary<string, object>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                fields[name] = TrakHoundTimeSeriesJson.Read(ref reader);
            }

            return fields;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var field in value)
            {
                writer.WritePropertyName(field.Key);
                TrakHoundTimeSeriesJson.Write(writer, field.Value);
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The Tags of a Query. Each can be one string or an array of strings
    /// </summary>
    public class TrakHoundTimeSeriesTagFilterJsonConverter : JsonConverter<Dictionary<string, string[]>>
    {
        /// <inheritdoc/>
        public override Dictionary<string, string[]> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Tags must be an object");

            var tags = new Dictionary<string, string[]>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();

                if (reader.TokenType == JsonTokenType.String)
                {
                    tags[name] = new[] { reader.GetString() };
                    continue;
                }

                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A Tag must be a string or an array of strings");

                var values = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String) throw new JsonException("A Tag must be a string or an array of strings");
                    values.Add(reader.GetString());
                }

                tags[name] = values.ToArray();
            }

            return tags;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Dictionary<string, string[]> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var tag in value)
            {
                writer.WritePropertyName(tag.Key);
                writer.WriteStartArray();
                foreach (var item in tag.Value) writer.WriteStringValue(item);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The Rows of a Result
    /// </summary>
    public class TrakHoundTimeSeriesRowsJsonConverter : JsonConverter<object[][]>
    {
        /// <inheritdoc/>
        public override object[][] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Rows must be an array");

            var rows = new List<object[]>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A Row must be an array");

                var row = new List<object>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    row.Add(TrakHoundTimeSeriesJson.Read(ref reader));
                }

                rows.Add(row.ToArray());
            }

            return rows.ToArray();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, object[][] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var row in value)
            {
                writer.WriteStartArray();
                foreach (var cell in row)
                {
                    TrakHoundTimeSeriesJson.Write(writer, cell);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
    }
}
