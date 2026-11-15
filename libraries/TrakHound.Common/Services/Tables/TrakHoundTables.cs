// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Gets Table Definitions and rows from classes, using the Table, Column, PrimaryKey, NotNull, Update, IgnoreDuplicateKey and TableIndex attributes.
    /// Names are converted to snake_case unless set by an attribute (attribute names are used as written)
    /// </summary>
    public static partial class TrakHoundTables
    {
        private static readonly ConcurrentDictionary<Type, TableInfo> _tables = new ConcurrentDictionary<Type, TableInfo>();


        private sealed class TableInfo
        {
            public TrakHoundTableDefinition Definition { get; init; }
            public PropertyInfo[] Properties { get; init; } // In the order of the Definition's Columns
        }


        /// <summary>
        /// Gets the Definition of the Table for T (see TrakHoundTableDefinition)
        /// </summary>
        public static TrakHoundTableDefinition GetDefinition<T>() => GetTable(typeof(T)).Definition;

        /// <summary>
        /// Gets the row of each item, with its value for each Column of the Definition of T (converted by GetColumnValue, and null for DBNull)
        /// </summary>
        public static object[][] GetRows<T>(IEnumerable<T> items)
        {
            var properties = GetTable(typeof(T)).Properties;
            return items.Select(item =>
            {
                var row = new object[properties.Length];
                for (var i = 0; i < row.Length; i++)
                {
                    var value = GetColumnValue(properties[i].PropertyType, properties[i].GetValue(item));
                    switch (value)
                    {
                        case DBNull: value = null; break;
                        case double d when !double.IsFinite(d): throw new ArgumentException($"{d} can't be stored in a Column");
                        case float f when !float.IsFinite(f): throw new ArgumentException($"{f} can't be stored in a Column");
                        case ulong u: value = (decimal)u; break; // Database Parameters have no mapping for ulong
                    }
                    row[i] = value;
                }
                return row;
            }).ToArray();
        }

        /// <summary>
        /// Gets the Column names for the properties of T (using the ColumnAttribute name when set)
        /// </summary>
        public static IEnumerable<string> GetColumnNames<T>()
        {
            return GetTable(typeof(T)).Definition.Columns.Select(o => o.Name).ToList();
        }

        /// <summary>
        /// Converts the input to a Table name (snake_case)
        /// </summary>
        public static string GetTableName(string input) => ToSnakeCase(input);

        /// <summary>
        /// Converts the input to a Schema name (snake_case)
        /// </summary>
        public static string GetSchemaName(string input) => ToSnakeCase(input);

        /// <summary>
        /// Converts the input to a Column name (snake_case)
        /// </summary>
        public static string GetColumnName(string input) => ToSnakeCase(input);

        /// <summary>
        /// Gets the Column name for the property (the ColumnAttribute name when set, otherwise the snake_case property name)
        /// </summary>
        public static string GetColumnName(PropertyInfo propertyInfo)
        {
            if (propertyInfo?.Name == null) return null;

            var columnAttribute = propertyInfo.GetCustomAttribute<ColumnAttribute>();
            return !string.IsNullOrEmpty(columnAttribute?.Name) ? columnAttribute.Name : GetColumnName(propertyInfo.Name);
        }

        /// <summary>
        /// Gets the Data Type used for the specified CLR type (Nullable types use their underlying type).
        /// DateTime, DateTimeOffset and TimeSpan are stored as Int64 nanoseconds, and strings, enums and other types as String
        /// </summary>
        public static TrakHoundTableDataType GetColumnType(Type input) => CreateColumnDefinition(input).DataType;

        /// <summary>
        /// Returns true if a Column for the specified CLR type allows null (reference types such as string, and Nullable types)
        /// </summary>
        public static bool GetColumnAllowNull(Type input)
        {
            return !input.IsValueType || Nullable.GetUnderlyingType(input) != null;
        }

        /// <summary>
        /// Returns the value, or DBNull if the value is null
        /// </summary>
        public static object GetColumnValue(object inputValue)
        {
            return inputValue ?? DBNull.Value;
        }

        /// <summary>
        /// Converts the value to the value stored in a Column of the specified type (DateTime and DateTimeOffset to Unix nanoseconds,
        /// TimeSpan to nanoseconds, bool to 1 or 0, enums and char to strings, and null or empty strings to DBNull)
        /// </summary>
        public static object GetColumnValue(Type inputType, object inputValue)
        {
            switch (inputValue)
            {
                case null: return DBNull.Value;
                case string s: return !string.IsNullOrEmpty(s) ? s : DBNull.Value;
                case DateTime d: return d.ToUnixTime();
                case DateTimeOffset d: return d.UtcDateTime.ToUnixTime();
                case TimeSpan t: return ToNanoseconds(t);
                case bool b: return b ? 1 : 0;
                case char c: return c.ToString();
                case Enum e: return e.ToString();
                default: return inputValue;
            }
        }


        private static TableInfo GetTable(Type type) => _tables.GetOrAdd(type, CreateTable);

        private static TableInfo CreateTable(Type type)
        {
            var tableAttribute = type.GetCustomAttribute<TableAttribute>();

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(o => o.GetMethod != null && o.GetMethod.IsPublic && o.GetIndexParameters().Length == 0)
                .ToArray();

            var columns = properties.Select(CreateColumn).ToArray();
            var tableName = !string.IsNullOrEmpty(tableAttribute?.Name) ? tableAttribute.Name : GetTableName(type.Name);

            return new TableInfo
            {
                Properties = properties,
                Definition = new TrakHoundTableDefinition
                {
                    Name = tableName,
                    Schema = !string.IsNullOrEmpty(tableAttribute?.Schema) ? tableAttribute.Schema : null,
                    IgnoreDuplicateKey = type.GetCustomAttribute<IgnoreDuplicateKeyAttribute>() != null,
                    Columns = columns,
                    Indexes = type.GetCustomAttributes<TableIndexAttribute>().Select(o => CreateIndex(tableName, columns, o)).ToArray()
                }
            };
        }

        // The Column for a CLR type: String defaults to a length of 255
        private static TrakHoundTableColumnDefinition CreateColumnDefinition(Type input)
        {
            var type = Nullable.GetUnderlyingType(input) ?? input;
            var definition = new TrakHoundTableColumnDefinition();

            if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(uint) || type == typeof(long)) definition.DataType = TrakHoundTableDataType.Int64;
            else if (type == typeof(bool)) definition.DataType = TrakHoundTableDataType.Boolean;
            else if (type == typeof(byte)) definition.DataType = TrakHoundTableDataType.Byte;
            else if (type == typeof(sbyte) || type == typeof(short)) definition.DataType = TrakHoundTableDataType.Int16;
            else if (type == typeof(ushort) || type == typeof(int)) definition.DataType = TrakHoundTableDataType.Int32;
            else if (type == typeof(float) || type == typeof(double)) definition.DataType = TrakHoundTableDataType.Float;
            else if (type == typeof(Guid)) definition.DataType = TrakHoundTableDataType.Guid;
            else if (type == typeof(ulong))
            {
                definition.DataType = TrakHoundTableDataType.Decimal;
                definition.Precision = 20;
                definition.Scale = 0;
            }
            else if (type == typeof(decimal))
            {
                definition.DataType = TrakHoundTableDataType.Decimal;
                definition.Precision = 38;
                definition.Scale = 10;
            }
            else if (type == typeof(char))
            {
                definition.DataType = TrakHoundTableDataType.String;
                definition.Length = 1;
                definition.FixedLength = true;
                definition.Unicode = true;
            }
            else
            {
                definition.DataType = TrakHoundTableDataType.String;
                definition.Length = 255;
            }

            return definition;
        }

        private static TrakHoundTableColumnDefinition CreateColumn(PropertyInfo property)
        {
            var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
            var primaryKeyAttribute = property.GetCustomAttribute<PrimaryKeyAttribute>();
            var notNull = property.GetCustomAttribute<NotNullAttribute>() != null;

            var definition = CreateColumnDefinition(property.PropertyType);
            definition.Name = GetColumnName(property);
            definition.AllowNull = GetColumnAllowNull(property.PropertyType) && !notNull && primaryKeyAttribute == null;
            definition.IsPrimaryKey = primaryKeyAttribute != null;
            definition.PrimaryKeyOrder = primaryKeyAttribute?.Order;
            definition.IsUpdate = property.GetCustomAttribute<UpdateAttribute>() != null;
            if (definition.DataType == TrakHoundTableDataType.String)
            {
                if (columnAttribute != null && columnAttribute.Length != 0) definition.Length = columnAttribute.Length;
                if (columnAttribute != null && columnAttribute.Unicode) definition.Unicode = true;
            }

            return definition;
        }

        private static TrakHoundTableIndex CreateIndex(string tableName, TrakHoundTableColumnDefinition[] columns, TableIndexAttribute attribute)
        {
            if (string.IsNullOrEmpty(attribute.Name) || attribute.Columns.IsNullOrEmpty())
            {
                throw new InvalidOperationException($"Invalid TableIndex on {tableName}: an Index needs a name and at least one Column");
            }

            // Catch misspelled Columns here instead of when the Table is created
            void ValidateColumn(string columnName)
            {
                if (!columns.Any(o => o.Name == columnName))
                {
                    throw new InvalidOperationException($"Invalid TableIndex {attribute.Name} on {tableName}: no Column named {columnName}");
                }
            }

            var indexColumns = attribute.Columns.Select(ParseIndexColumn).ToArray();
            foreach (var indexColumn in indexColumns) ValidateColumn(indexColumn.Name);
            if (attribute.Include != null) foreach (var includeColumn in attribute.Include) ValidateColumn(includeColumn);

            return new TrakHoundTableIndex
            {
                Name = attribute.Name,
                Type = TrakHoundTableIndexType.NonClustered,
                IsUnique = attribute.IsUnique,
                Columns = indexColumns,
                IncludeColumns = attribute.Include
            };
        }

        // "name", "name asc" or "name desc"
        private static TrakHoundTableIndexColumn ParseIndexColumn(string input)
        {
            var value = input?.Trim() ?? "";
            if (value.EndsWith(" desc", StringComparison.OrdinalIgnoreCase)) return new TrakHoundTableIndexColumn(value.Substring(0, value.Length - 5).TrimEnd(), TrakHoundSortOrder.Descending);
            if (value.EndsWith(" asc", StringComparison.OrdinalIgnoreCase)) return new TrakHoundTableIndexColumn(value.Substring(0, value.Length - 4).TrimEnd(), TrakHoundSortOrder.Ascending);
            return new TrakHoundTableIndexColumn(value, null);
        }

        private static string ToSnakeCase(string input) => input?.ToUnderscoreUpper()?.ToLower();

        private static long ToNanoseconds(TimeSpan value)
        {
            // Clamp so TimeSpan.MinValue/MaxValue don't overflow
            if (value.Ticks > long.MaxValue / 100) return long.MaxValue;
            if (value.Ticks < long.MinValue / 100) return long.MinValue;
            return value.Ticks * 100;
        }
    }
}
