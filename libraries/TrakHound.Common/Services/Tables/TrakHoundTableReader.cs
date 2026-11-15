// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Reads Table rows into objects. Columns are matched to properties by Column name (ignoring case, like SQL Server),
    /// and values are converted the way TrakHoundTables writes them (ex. bigint nanoseconds to DateTime and TimeSpan).
    /// Scalar types (ex. string, long, Guid) read the first Column
    /// </summary>
    internal static class TrakHoundTableReader
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> _properties = new ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>>();


        /// <summary>
        /// Reads each (non-null) row of the result set. Throws a FormatException if a value can't be converted to its property type
        /// </summary>
        public static IEnumerable<T> Read<T>(TrakHoundTableResult tableResult)
        {
            if (tableResult?.Rows == null) yield break;

            var readRow = CreateRowReader<T>(tableResult.Columns);

            foreach (var row in tableResult.Rows)
            {
                if (row != null) yield return readRow(row);
            }
        }

        /// <summary>
        /// Reads the first Column of the row as a string (null if the row is empty or the value is null or empty)
        /// </summary>
        public static string ReadString(object[] row)
        {
            return row != null && row.Length > 0 ? (string)ConvertValue(row[0], typeof(string), null) : null;
        }

        /// <summary>
        /// Converts a Column value to the specified type (null for null values). Throws a FormatException if it can't be converted
        /// </summary>
        public static object ConvertValue(object value, Type type, string columnName)
        {
            if (value == null || value is DBNull) return null;

            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            try
            {
                return ConvertTo(value, targetType);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
            {
                throw new FormatException($"Column '{columnName}' value '{value}' can't be read as {targetType.Name}", ex);
            }
        }


        private static Func<object[], T> CreateRowReader<T>(TrakHoundTableColumn[] columns)
        {
            var type = typeof(T);

            if (IsScalar(type))
            {
                var columnName = columns != null && columns.Length > 0 ? columns[0]?.Name : null;
                return row =>
                {
                    var value = row.Length > 0 ? ConvertValue(row[0], type, columnName) : null;
                    return value != null ? (T)value : default;
                };
            }

            // Match each Column to a property once per result set
            var properties = _properties.GetOrAdd(type, GetProperties);
            var columnProperties = new PropertyInfo[columns?.Length ?? 0];
            for (var i = 0; i < columnProperties.Length; i++)
            {
                var name = columns[i]?.Name;
                if (name != null && properties.TryGetValue(name, out var property)) columnProperties[i] = property;
            }

            return row =>
            {
                var obj = CreateInstance(type);

                var count = Math.Min(row.Length, columnProperties.Length);
                for (var i = 0; i < count; i++)
                {
                    var property = columnProperties[i];
                    if (property == null) continue;

                    var value = ConvertValue(row[i], property.PropertyType, columns[i].Name);

                    // Leave non-nullable value types at their default
                    if (value == null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null) continue;

                    property.SetValue(obj, value);
                }

                return (T)obj;
            };
        }

        private static object CreateInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception ex) when (ex is MissingMethodException || ex is MemberAccessException)
            {
                throw new InvalidOperationException($"{type.Name} can't be read from a Table: it needs a public parameterless constructor", ex);
            }
        }

        private static IReadOnlyDictionary<string, PropertyInfo> GetProperties(Type type)
        {
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod != null && property.GetIndexParameters().Length == 0)
                {
                    properties.TryAdd(TrakHoundTables.GetColumnName(property), property);
                }
            }

            return properties;
        }

        private static bool IsScalar(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;

            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) ||
                t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[]);
        }

        private static object ConvertTo(object value, Type type)
        {
            if (type == typeof(string))
            {
                var s = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
                return !string.IsNullOrEmpty(s) ? s : null;
            }

            if (type == typeof(object)) return value;
            if (type == typeof(bool)) return value.ToBoolean();

            // DateTime, DateTimeOffset and TimeSpan Columns are bigint nanoseconds (see TrakHoundTables.GetColumnValue)
            if (type == typeof(DateTime))
            {
                if (value is DateTime dateTime) return dateTime;
                if (value is DateTimeOffset dateTimeOffset) return dateTimeOffset.UtcDateTime;
                if (TryGetInt64(value, out var n)) return UnixTimeExtensions.FromUnixTime(n);
                return DateTime.Parse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (type == typeof(DateTimeOffset))
            {
                if (value is DateTimeOffset dateTimeOffset) return dateTimeOffset;
                if (value is DateTime dateTime) return new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime);
                if (TryGetInt64(value, out var n)) return new DateTimeOffset(UnixTimeExtensions.FromUnixTime(n));
                return DateTimeOffset.Parse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            if (type == typeof(TimeSpan))
            {
                if (value is TimeSpan timeSpan) return timeSpan;
                if (TryGetInt64(value, out var n)) return TimeSpan.FromTicks(n / 100);
                return TimeSpan.Parse(value.ToString(), CultureInfo.InvariantCulture);
            }

            if (type == typeof(Guid))
            {
                if (value is Guid guid) return guid;
                return Guid.Parse(value.ToString());
            }

            if (type == typeof(char))
            {
                if (value is char c) return c;
                var s = value.ToString();
                if (s.Length == 1) return s[0];
                throw new FormatException("Expected a single character");
            }

            if (type.IsEnum)
            {
                if (value is string s) return Enum.Parse(type, s, true);
                return Enum.ToObject(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            if (type == typeof(byte[]) && value is string base64) return Convert.FromBase64String(base64);

            if (type.IsInstanceOfType(value)) return value;

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        private static bool TryGetInt64(object value, out long result)
        {
            switch (value)
            {
                case long x: result = x; return true;
                case int x: result = x; return true;
                case short x: result = x; return true;
                case sbyte x: result = x; return true;
                case byte x: result = x; return true;
                case ushort x: result = x; return true;
                case uint x: result = x; return true;
                case ulong x when x <= long.MaxValue: result = (long)x; return true;
                case decimal x when x == decimal.Truncate(x) && x >= long.MinValue && x <= long.MaxValue: result = (long)x; return true;
                case double x when x == Math.Truncate(x) && x >= long.MinValue && x < long.MaxValue: result = (long)x; return true;
                case string s: return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
                default: result = 0; return false;
            }
        }
    }
}
