// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// Helpers for serializing and deserializing JSON and reading/writing JSON files
    /// </summary>
    public static class Json
    {
        #region "Options"

        // Options are cached because System.Text.Json caches Type metadata per JsonSerializerOptions instance
        private static readonly JsonSerializerOptions _options = CreateOptions(false, null);
        private static readonly JsonSerializerOptions _indentedOptions = CreateOptions(true, null);

        // Options for each JsonConverter instance ([0] = compact, [1] = indented)
        private static readonly ConditionalWeakTable<JsonConverter, JsonSerializerOptions[]> _converterOptions = new ConditionalWeakTable<JsonConverter, JsonSerializerOptions[]>();


        /// <summary>
        /// Gets a new (modifiable) instance of the default JsonSerializerOptions (camelCase, case-insensitive property names, default values ignored when writing, numbers readable from strings).
        /// Use SharedOptions when the options don't need to be modified
        /// </summary>
        public static JsonSerializerOptions DefaultOptions => new JsonSerializerOptions(_options);

        /// <summary>
        /// Gets the cached, read-only default JsonSerializerOptions (the same settings as DefaultOptions). Reusing this instance avoids rebuilding Type metadata on each call
        /// </summary>
        public static JsonSerializerOptions SharedOptions => _options;

        private static JsonSerializerOptions CreateOptions(bool indented, JsonConverter converter)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                MaxDepth = 1000,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver() // Explicit so the options also work in applications that disable reflection by default
            };

            if (converter != null) options.Converters.Add(converter);

            options.MakeReadOnly();
            return options;
        }

        private static JsonSerializerOptions GetOptions(JsonConverter converter, bool indented)
        {
            if (converter == null) return indented ? _indentedOptions : _options;

            var cached = _converterOptions.GetValue(converter, o => new JsonSerializerOptions[2]);
            var index = indented ? 1 : 0;
            return cached[index] ??= CreateOptions(indented, converter);
        }

        #endregion

        #region "Conversion"

        /// <summary>
        /// Serializes the object to a JSON string (camelCase), optionally using the specified JsonConverter and indented formatting (null if the object is null or serialization fails)
        /// </summary>
        public static string Convert(object obj, JsonConverter converter = null, bool indented = false)
        {
            if (obj != null)
            {
                try
                {
                    return JsonSerializer.Serialize(obj, GetOptions(converter, indented));
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Serializes the object to UTF-8 JSON bytes using the default options (empty if the object is null or serialization fails)
        /// </summary>
        public static byte[] ConvertToUtf8(object obj)
        {
            if (obj != null)
            {
                try
                {
                    return JsonSerializer.SerializeToUtf8Bytes(obj, _options);
                }
                catch { }
            }

            return Array.Empty<byte>();
        }

        /// <summary>
        /// Serializes the object to UTF-8 JSON bytes (camelCase), optionally indented. Returns null if the object is null or serialization fails, the same as Convert(...).ToUtf8Bytes() without the intermediate string
        /// </summary>
        public static byte[] ConvertToUtf8Bytes(object obj, bool indented = false)
        {
            if (obj != null)
            {
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, indented ? _indentedOptions : _options);
                    return bytes.Length > 0 ? bytes : null;
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Deserializes the JSON string to an object of Type T using the default options (default if the string is empty or invalid)
        /// </summary>
        public static T Convert<T>(string json)
        {
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(json, _options);
                }
                catch { }
            }

            return default(T);
        }

        /// <summary>
        /// Deserializes the UTF-8 JSON bytes to an object of Type T using the specified JsonTypeInfo (default if empty or invalid)
        /// </summary>
        public static T Convert<T>(ReadOnlySpan<byte> json, JsonTypeInfo<T> typeInfo)
        {
            if (!json.IsEmpty)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(json, typeInfo);
                }
                catch { }
            }

            return default(T);
        }

        /// <summary>
        /// Deserializes each JSON string to an object of Type T, skipping empty or invalid strings (null if the list is empty)
        /// </summary>
        public static IEnumerable<T> Convert<T>(IEnumerable<string> jsons)
        {
            if (!jsons.IsNullOrEmpty())
            {
                var objs = new List<T>();

                foreach (var json in jsons)
                {
                    if (!string.IsNullOrEmpty(json))
                    {
                        var obj = Convert<T>(json);
                        if (obj != null) objs.Add(obj);
                    }
                }

                return objs;
            }

            return null;
        }

        /// <summary>
        /// Converts the object to Type T by serializing it to JSON (using the JsonConverter if specified) and deserializing the result (default if the conversion fails)
        /// </summary>
        public static T ConvertObject<T>(object obj, JsonConverter converter = null)
        {
            if (obj != null)
            {
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, GetOptions(converter, false));
                    return JsonSerializer.Deserialize<T>(bytes, _options);
                }
                catch { }
            }

            return default(T);
        }

        /// <summary>
        /// Converts each object in the list to Type T by serializing it to JSON and deserializing the result, skipping objects that fail to convert
        /// </summary>
        public static IEnumerable<T> ConvertList<T>(IEnumerable objs, JsonConverter converter = null)
        {
            if (objs != null)
            {
                var rObjs = new List<T>();

                foreach (var obj in objs)
                {
                    var rObj = ConvertObject<T>(obj, converter);
                    if (rObj != null) rObjs.Add(rObj);
                }

                return rObjs;
            }

            return null;
        }

        /// <summary>
        /// Deserializes the JSON string to an object of Type T using the default options. Returns false and the Exception if the string is empty or deserialization fails
        /// </summary>
        public static bool TryDeserialize<T>(string json, out T result, out Exception exception)
        {
            result = default;
            exception = null;

            if (string.IsNullOrEmpty(json))
            {
                exception = new ArgumentException("JSON is empty", nameof(json));
                return false;
            }

            try
            {
                result = JsonSerializer.Deserialize<T>(json, _options);
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        /// <summary>
        /// Serializes the object to a JSON string (camelCase), optionally indented. Returns false and the Exception if the object is null or serialization fails
        /// </summary>
        public static bool TrySerialize(object obj, out string json, out Exception exception, bool indented = false)
        {
            json = null;
            exception = null;

            if (obj == null)
            {
                exception = new ArgumentNullException(nameof(obj));
                return false;
            }

            try
            {
                json = JsonSerializer.Serialize(obj, GetOptions(null, indented));
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        /// <summary>
        /// Builds a JSON array string from the values. Numbers and bools are written unquoted (NaN and Infinity as null),
        /// other values as escaped strings using their Invariant Culture string representation
        /// </summary>
        public static string ToJsonArray(object[] array)
        {
            if (array != null)
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    WriteArrayValues(writer, array);
                }
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return null;
        }

        /// <summary>
        /// Writes the values as a JSON array (see ToJsonArray(object[])) to the Stream and returns the number of bytes written.
        /// The buffer is not used (kept for compatibility)
        /// </summary>
        public static int ToJsonArray(Stream outputStream, object[] array, ref byte[] buffer)
        {
            if (outputStream != null && array != null)
            {
                using var writer = new Utf8JsonWriter(outputStream);
                WriteArrayValues(writer, array);
                writer.Flush();
                return (int)writer.BytesCommitted;
            }

            return 0;
        }

        private static void WriteArrayValues(Utf8JsonWriter writer, object[] array)
        {
            writer.WriteStartArray();
            foreach (var obj in array)
            {
                if (!TryWritePrimitive(writer, obj))
                {
                    var s = obj is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : obj.ToString();
                    writer.WriteStringValue(s);
                }
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Writes null, string, bool, and numeric values (NaN and Infinity as null). Returns false for other Types
        /// </summary>
        internal static bool TryWritePrimitive(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); return true;
                case string s: writer.WriteStringValue(s); return true;
                case bool b: writer.WriteBooleanValue(b); return true;
                case byte x: writer.WriteNumberValue(x); return true;
                case sbyte x: writer.WriteNumberValue(x); return true;
                case short x: writer.WriteNumberValue(x); return true;
                case ushort x: writer.WriteNumberValue(x); return true;
                case int x: writer.WriteNumberValue(x); return true;
                case uint x: writer.WriteNumberValue(x); return true;
                case long x: writer.WriteNumberValue(x); return true;
                case ulong x: writer.WriteNumberValue(x); return true;
                case decimal x: writer.WriteNumberValue(x); return true;
                case float x:
                    if (float.IsFinite(x)) writer.WriteNumberValue(x);
                    else writer.WriteNullValue();
                    return true;
                case double x:
                    if (double.IsFinite(x)) writer.WriteNumberValue(x);
                    else writer.WriteNullValue();
                    return true;
                default: return false;
            }
        }

        #endregion

        #region "IO"

        /// <summary>
        /// Reads the JSON file at the specified path and deserializes it to Type T (default if the file does not exist or is invalid)
        /// </summary>
        public static T Read<T>(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return JsonSerializer.Deserialize<T>(stream, _options);
                }
                catch { }
            }

            return default;
        }

        /// <summary>
        /// Reads the JSON file at the specified path and deserializes the specified top-level property (case-insensitive) to Type T
        /// (default if the file or property does not exist or is invalid)
        /// </summary>
        public static T Read<T>(string path, string node)
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(node) && File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    using var document = JsonDocument.Parse(stream);

                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in document.RootElement.EnumerateObject())
                        {
                            if (string.Equals(property.Name, node, StringComparison.OrdinalIgnoreCase))
                            {
                                return property.Value.Deserialize<T>(_options);
                            }
                        }
                    }
                }
                catch { }
            }

            return default;
        }

        /// <summary>
        /// Asynchronously reads the JSON file at the specified path and deserializes it to Type T (default if the file does not exist or is invalid)
        /// </summary>
        public static Task<T> ReadAsync<T>(string path) => ReadAsync<T>(path, CancellationToken.None);

        /// <summary>
        /// Asynchronously reads the JSON file at the specified path and deserializes it to Type T (default if the file does not exist, is invalid, or the read is cancelled)
        /// </summary>
        public static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    // A synchronous Dispose is enough for a read, and FileStream has no DisposeAsync on .NET Standard 2.0 (TrakHound.Client)
                    using var stream = File.OpenRead(path);
                    return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
                }
                catch { }
            }

            return default;
        }


        /// <summary>
        /// Writes the object as indented JSON to the specified file path, creating the directory if needed (errors are ignored)
        /// </summary>
        public static void Write(object obj, string path) => Write(obj, path, false);

        /// <summary>
        /// Writes the object as indented JSON to the specified file path, creating the directory if needed (errors are ignored).
        /// When atomic is true, the JSON is written to a temporary file that then replaces the file, so a failed write can't leave a partial file.
        /// Note: an atomic write raises a Renamed (not Changed) event for FileSystemWatchers
        /// </summary>
        public static void Write(object obj, string path, bool atomic)
        {
            if (obj != null && !string.IsNullOrEmpty(path))
            {
                // Serialize first so the file isn't touched if serialization fails
                var json = Convert(obj, indented: true);
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        CreateDirectory(path);

                        if (atomic)
                        {
                            var tempPath = GetTempPath(path);
                            try
                            {
                                File.WriteAllText(tempPath, json);
                                File.Move(tempPath, path, true);
                            }
                            finally
                            {
                                DeleteTempFile(tempPath);
                            }
                        }
                        else
                        {
                            File.WriteAllText(path, json);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Asynchronously writes the object as indented JSON to the specified file path, creating the directory if needed (errors are ignored)
        /// </summary>
        public static Task WriteAsync(object obj, string path) => WriteAsync(obj, path, false, CancellationToken.None);

        /// <summary>
        /// Asynchronously writes the object as indented JSON to the specified file path, creating the directory if needed (errors are ignored)
        /// </summary>
        public static Task WriteAsync(object obj, string path, CancellationToken cancellationToken) => WriteAsync(obj, path, false, cancellationToken);

        /// <summary>
        /// Asynchronously writes the object as indented JSON to the specified file path, creating the directory if needed (errors are ignored).
        /// When atomic is true, the JSON is written to a temporary file that then replaces the file, so a failed write can't leave a partial file.
        /// Note: an atomic write raises a Renamed (not Changed) event for FileSystemWatchers
        /// </summary>
        public static async Task WriteAsync(object obj, string path, bool atomic, CancellationToken cancellationToken = default)
        {
            if (obj != null && !string.IsNullOrEmpty(path))
            {
                // Serialize first so the file isn't touched if serialization fails
                var json = Convert(obj, indented: true);
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        CreateDirectory(path);

                        if (atomic)
                        {
                            var tempPath = GetTempPath(path);
                            try
                            {
                                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                                File.Move(tempPath, path, true);
                            }
                            finally
                            {
                                DeleteTempFile(tempPath);
                            }
                        }
                        else
                        {
                            await File.WriteAllTextAsync(path, json, cancellationToken);
                        }
                    }
                    catch { }
                }
            }
        }

        private static void CreateDirectory(string path)
        {
            // A bare file name (ex. "file.json") has no directory to create
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }

        private static string GetTempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

        private static void DeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
        }

        #endregion
    }

    /// <summary>
    /// JsonConverter that reads and writes a jagged object array (object[][]) as a JSON array of arrays
    /// </summary>
    public class ObjectJaggedArrayJsonConverter : JsonConverter<object[][]>
    {
        /// <summary>
        /// Reads a JSON array of arrays into an object[][]. Cells are read as strings, booleans, nulls, numbers (int, long, or double),
        /// or JsonElement for nested objects and arrays. A null row is read as null
        /// </summary>
        public override object[][] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of rows");

            var rows = new List<object[]>();

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.EndArray:
                        return rows.ToArray();

                    case JsonTokenType.Null:
                        rows.Add(null);
                        break;

                    case JsonTokenType.StartArray:
                        var row = new List<object>();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            row.Add(ReadCell(ref reader));
                        }
                        rows.Add(row.ToArray());
                        break;

                    default:
                        throw new JsonException("Expected a row array");
                }
            }

            throw new JsonException("Unexpected end of rows");
        }

        private static object ReadCell(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Null: return null;
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var valueInt)) return valueInt;
                    if (reader.TryGetInt64(out var valueInt64)) return valueInt64;
                    return reader.GetDouble();
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    // Reads the whole nested value (and advances the reader to its end)
                    return JsonElement.ParseValue(ref reader);
                default:
                    throw new JsonException($"Unexpected token in row : {reader.TokenType}");
            }
        }

        /// <summary>
        /// Writes the rows as a JSON array of arrays (an empty array if there are no rows). A null row is written as null.
        /// Cells that aren't strings, bools, numbers, or DateTimes are written using the JsonSerializer (ex. Guid, DateTimeOffset, byte[])
        /// </summary>
        public override void Write(Utf8JsonWriter writer, object[][] rows, JsonSerializerOptions options)
        {
            if (rows == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            foreach (var row in rows)
            {
                if (row == null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                writer.WriteStartArray();
                foreach (var cell in row)
                {
                    if (Json.TryWritePrimitive(writer, cell)) continue;

                    if (cell is DateTime dateTime) writer.WriteStringValue(dateTime);
                    else JsonSerializer.Serialize(writer, cell, cell.GetType(), options);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
    }
}
