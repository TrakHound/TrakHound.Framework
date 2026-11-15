// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TrakHound.Services.Tables.Attributes;

#pragma warning disable CS0618 // The helpers below only exist for the obsolete members

namespace TrakHound.Services.Tables
{
    // The SQL Server script generation that modules used before Table Requests. Packages that were built against it keep
    // working on SQL Server until they move to TrakHoundTableRequestBuilder. Remove this file when none of them use it
    public static partial class TrakHoundTables
    {
        private const string _obsoleteMessage = "Generates SQL that only runs on SQL Server. Use TrakHoundTableRequestBuilder";

        private const string _newLine = "\r\n";

        // SQL Server allows up to 1000 rows in a single values list
        private const int _maxValuesRows = 1000;

        private static readonly ConcurrentDictionary<Type, TableDefinition> _definitions = new ConcurrentDictionary<Type, TableDefinition>();


        private sealed class TableDefinition
        {
            public string Name { get; init; }
            public string Schema { get; init; }
            public bool IgnoreDuplicateKey { get; init; }
            public TableColumn[] Columns { get; init; }
            public TableColumn[] PrimaryKeys { get; init; }
            public int[] PrimaryKeyIndexes { get; init; } // Positions of the PrimaryKeys in Columns
            public TrakHoundTableIndex[] Indexes { get; init; }
        }

        private sealed class TableColumn
        {
            public PropertyInfo Property { get; init; }
            public string Name { get; init; }
            public string Identifier { get; init; }
            public string Definition { get; init; }
            public bool AllowNull { get; init; }
            public bool IsPrimaryKey { get; init; }
            public TrakHoundSortOrder? Order { get; init; }
            public bool IsUpdate { get; init; }

            public string GetValue(object model) => FormatValue(GetColumnValue(Property.PropertyType, Property.GetValue(model)));
        }


        /// <summary>
        /// Creates a new TrakHoundTableQueryBuilder with the default Batch Size
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static TrakHoundTableQueryBuilder CreateBuilder()
        {
            return new TrakHoundTableQueryBuilder();
        }

        /// <summary>
        /// Generates a script that creates the Schema, Table and TableIndex Indexes for TInput if they do not exist
        /// (name and schema override the TableAttribute and class name)
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static string GenerateCreate<TInput>(string name = null, string schema = null)
        {
            var definition = GetSqlServerDefinition(typeof(TInput));
            var tableName = !string.IsNullOrEmpty(name) ? name : definition.Name;
            var schemaName = !string.IsNullOrEmpty(schema) ? schema : definition.Schema;
            var tableIdentifier = GetTableIdentifier(tableName, schemaName);

            var columns = definition.Columns.Select(o => $"{o.Identifier} {o.Definition}{(o.AllowNull ? "" : " NOT NULL")}");

            var queryLines = new List<string>();

            // Add Create Schema Query
            if (!string.IsNullOrEmpty(schemaName))
            {
                queryLines.Add($"if not exists (select * from sys.schemas where [name] = {QuoteString(schemaName)})");
                queryLines.Add("begin");
                queryLines.Add($"   EXEC({QuoteString($"create schema {GetSchemaIdentifier(schemaName)};")});");
                queryLines.Add("end;");
            }

            // Add Create Table Query
            var schemaId = !string.IsNullOrEmpty(schemaName) ? $"SCHEMA_ID({QuoteString(schemaName)})" : "SCHEMA_ID()";
            queryLines.Add($"if not exists (select * from sys.tables where [schema_id] = {schemaId} and [name] = {QuoteString(tableName)})");
            queryLines.Add("begin");
            queryLines.Add($"   create table {tableIdentifier} ({string.Join(", ", columns)});");

            if (definition.PrimaryKeys.Length > 0)
            {
                var primaryKeyColumns = string.Join(", ", definition.PrimaryKeys.Select(GetPrimaryKeyColumn));
                var options = definition.IgnoreDuplicateKey ? " with (IGNORE_DUP_KEY = ON)" : "";

                queryLines.Add($"   alter table {tableIdentifier} add constraint {QuoteIdentifier($"{tableName}_primary_key")} primary key clustered ({primaryKeyColumns}){options};");
            }

            queryLines.Add("end;");

            // Add Create Index Queries (checked separately from the Table so Indexes are added to existing Tables)
            foreach (var index in definition.Indexes)
            {
                var indexType = index.Type == TrakHoundTableIndexType.Clustered ? "clustered" : "nonclustered";
                var unique = index.IsUnique ? "unique " : "";
                var indexColumns = string.Join(", ", index.Columns.Select(GetIndexColumn));
                var include = !index.IncludeColumns.IsNullOrEmpty() ? $" include ({string.Join(", ", index.IncludeColumns.Select(QuoteIdentifier))})" : "";

                queryLines.Add($"if not exists (select * from sys.indexes where [object_id] = OBJECT_ID({QuoteString(tableIdentifier)}) and [name] = {QuoteString(index.Name)})");
                queryLines.Add("begin");
                queryLines.Add($"   create {unique}{indexType} index {QuoteIdentifier(index.Name)} on {tableIdentifier} ({indexColumns}){include};");
                queryLines.Add("end;");
            }

            return string.Join(_newLine, queryLines);
        }

        /// <summary>
        /// Generates Insert Statements for the specified items (up to 1000 rows per Statement). When overwrite is true, existing rows
        /// with the same Primary Key are deleted before the Insert. Returns null if there are no items
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static string GenerateInsert<TInput>(IEnumerable<TInput> models, string tableName = null, string schemaName = null, bool overwrite = false)
        {
            return CreateInsert(models, null, tableName, schemaName, overwrite);
        }

        /// <summary>
        /// Generates Insert Statements for the specified items (up to 1000 rows per Statement) that reference their values as Parameters
        /// (@p0, @p1, ..., named by their position in parameters) instead of literals, so the same shape of Insert reuses its query plan.
        /// The Parameters are added to parameters, which must be passed with the Query (null writes literals, as GenerateInsert without parameters).
        /// SQL Server allows 2100 Parameters per command, so callers must keep the number of items times the number of Columns under that limit
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static string GenerateInsert<TInput>(IEnumerable<TInput> models, TrakHoundTableParameterCollection parameters, string tableName = null, string schemaName = null, bool overwrite = false)
        {
            return CreateInsert(models, parameters, tableName, schemaName, overwrite);
        }

        /// <summary>
        /// Generates a Merge Statement that updates existing rows (matched by Primary Key) and inserts new rows for the specified items.
        /// Returns null if there are no items. Throws an InvalidOperationException if TInput has no Primary Key
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static string GenerateMerge<TInput>(IEnumerable<TInput> models, string tableName = null, string schemaName = null)
        {
            return CreateMerge(models, null, tableName, schemaName);
        }

        /// <summary>
        /// Generates a Merge Statement (see GenerateMerge) that references the item values as Parameters (@p0, @p1, ...) added to parameters,
        /// which must be passed with the Query (null writes literals). Callers must keep the number of Parameters under SQL Server's limit of 2100
        /// </summary>
        [Obsolete(_obsoleteMessage)]
        public static string GenerateMerge<TInput>(IEnumerable<TInput> models, TrakHoundTableParameterCollection parameters, string tableName = null, string schemaName = null)
        {
            return CreateMerge(models, parameters, tableName, schemaName);
        }

        private static string CreateInsert<TInput>(IEnumerable<TInput> models, TrakHoundTableParameterCollection parameters, string tableName, string schemaName, bool overwrite)
        {
            if (models.IsNullOrEmpty()) return null;

            var definition = GetSqlServerDefinition(typeof(TInput));
            var target = GetTarget(definition, tableName, schemaName);
            var columns = string.Join($",{_newLine}", definition.Columns.Select(o => o.Identifier));

            var queries = new List<string>();

            foreach (var batch in models.Chunk(_maxValuesRows))
            {
                // The values of each row (literals, or Parameter references that the delete and insert share)
                var rowValues = batch.Select(model => GetValues(definition, model, parameters)).ToArray();

                // Delete existing rows with the same Primary Key
                if (overwrite && definition.PrimaryKeys.Length > 0)
                {
                    var keyColumns = string.Join(", ", definition.PrimaryKeys.Select(o => o.Identifier));
                    var keyRows = rowValues.Select(values => $"({string.Join(", ", definition.PrimaryKeyIndexes.Select(i => values[i]))})");
                    var keyConditions = string.Join(" and ", definition.PrimaryKeys.Select(o => $"[input].{o.Identifier} = [target].{o.Identifier}"));

                    queries.Add($"delete [target] from {target} as [target]{_newLine}inner join (values{_newLine}{string.Join($",{_newLine}", keyRows)}{_newLine}) as [input] ({keyColumns}){_newLine}on {keyConditions};");
                }

                var rows = rowValues.Select(values => $"({string.Join(", ", values)})");

                queries.Add($"insert into {target}{_newLine}({_newLine}{columns}{_newLine}){_newLine}values{_newLine}{string.Join($",{_newLine}", rows)};");
            }

            return string.Join($"{_newLine}{_newLine}", queries);
        }

        private static string CreateMerge<TInput>(IEnumerable<TInput> models, TrakHoundTableParameterCollection parameters, string tableName, string schemaName)
        {
            if (models.IsNullOrEmpty()) return null;

            var definition = GetSqlServerDefinition(typeof(TInput));
            if (definition.PrimaryKeys.Length == 0)
            {
                throw new InvalidOperationException($"Cannot generate a Merge for {typeof(TInput).Name}: it has no PrimaryKey properties");
            }

            var target = GetTarget(definition, tableName, schemaName);
            var columns = string.Join(", ", definition.Columns.Select(o => o.Identifier));

            // Name the variable by the Parameter position when using Parameters so the Query text repeats (and its plan is reused).
            // Each Merge adds Parameters, so the name is still unique within a script
            var inputVariable = parameters != null ? $"@merge_input_{parameters.Count}" : $"@{Guid.NewGuid().ToString().ToMD5Hash()}";

            var queryBuilder = new StringBuilder();

            // Add Table Variable (with the same Column definitions as the Table so values are not truncated)
            queryBuilder.Append($"declare {inputVariable} as table ({string.Join(", ", definition.Columns.Select(o => $"{o.Identifier} {o.Definition}"))});{_newLine}");

            // Add Values to Table Variable
            foreach (var batch in models.Chunk(_maxValuesRows))
            {
                var rows = batch.Select(model => $"({string.Join(", ", GetValues(definition, model, parameters))})");
                queryBuilder.Append($"insert into {inputVariable} ({columns}) values{_newLine}{string.Join($",{_newLine}", rows)};{_newLine}");
            }

            queryBuilder.Append(_newLine);

            // Add Merge Conditions
            var keyConditions = string.Join(" and ", definition.PrimaryKeys.Select(o => $"[input].{o.Identifier} = [target].{o.Identifier}"));
            queryBuilder.Append($"merge {target} as [target] using {inputVariable} as [input] on {keyConditions}{_newLine}{_newLine}");

            // Add Updates
            var updateColumns = definition.Columns.Where(o => !o.IsPrimaryKey).ToArray();
            if (updateColumns.Length > 0)
            {
                queryBuilder.Append("when matched");

                // Only update when an Update Column changed ("except" compares nulls as equal, unlike <>)
                var changeColumns = updateColumns.Where(o => o.IsUpdate).ToArray();
                if (changeColumns.Length > 0)
                {
                    var inputColumns = string.Join(", ", changeColumns.Select(o => $"[input].{o.Identifier}"));
                    var targetColumns = string.Join(", ", changeColumns.Select(o => $"[target].{o.Identifier}"));
                    queryBuilder.Append($" and exists (select {inputColumns} except select {targetColumns})");
                }

                var updateValues = updateColumns.Select(o => $"[target].{o.Identifier} = [input].{o.Identifier}");
                queryBuilder.Append($"{_newLine}then update set{_newLine}{string.Join($",{_newLine}", updateValues)}{_newLine}{_newLine}");
            }

            // Add Inserts
            var insertValues = string.Join(", ", definition.Columns.Select(o => $"[input].{o.Identifier}"));
            queryBuilder.Append($"when not matched by target{_newLine}then insert ({columns}){_newLine}values ({insertValues});{_newLine}");

            return queryBuilder.ToString();
        }

        /// <summary>
        /// Reads the contents of the specified script file (null if the file does not exist, is empty or can't be read)
        /// </summary>
        [Obsolete("Use TrakHoundTableQueryVariants.FromDirectory, which has a script for each Dialect")]
        public static string ReadScriptFromFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var contents = File.ReadAllText(filePath);
                        if (!string.IsNullOrEmpty(contents))
                        {
                            return contents;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Generates a Statement that declares an empty Table Variable with a single [value] Column of the specified SQL data type
        /// </summary>
        [Obsolete("Table Variables only exist on SQL Server")]
        public static string CreateTableVariable<TValue>(string variableName, string dataType)
        {
            return CreateTableVariable<TValue>(variableName, dataType, null);
        }

        /// <summary>
        /// Generates Statements that declare a Table Variable with a single [value] Column of the specified SQL data type
        /// and insert each of the specified values (null if variableName or dataType is not set)
        /// </summary>
        [Obsolete("Table Variables only exist on SQL Server")]
        public static string CreateTableVariable<TValue>(string variableName, string dataType, IEnumerable<TValue> values)
        {
            if (string.IsNullOrEmpty(variableName) || string.IsNullOrEmpty(dataType)) return null;

            var query = new StringBuilder();
            query.Append($"declare @{variableName} as table ([value] {dataType});");

            if (!values.IsNullOrEmpty())
            {
                foreach (var batch in values.Chunk(_maxValuesRows))
                {
                    var rows = batch.Select(o => $"({FormatValue(GetColumnValue(typeof(TValue), o))})");
                    query.Append($"{_newLine}insert into @{variableName} values {string.Join(", ", rows)};");
                }
            }

            return query.ToString();
        }

        /// <summary>
        /// Gets the bracketed Table Identifier, including the Schema when set (ex. [schema].[table])
        /// </summary>
        [Obsolete("Brackets only work on SQL Server. Write the name in double quotes, as in the \"ansi\" subset of SQL")]
        public static string GetTableIdentifier(string tableName, string schemaName = null)
        {
            if (!string.IsNullOrEmpty(schemaName))
            {
                return $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}";
            }
            else
            {
                return QuoteIdentifier(tableName);
            }
        }

        /// <summary>
        /// Gets the bracketed Schema Identifier (ex. [schema])
        /// </summary>
        [Obsolete("Brackets only work on SQL Server. Write the name in double quotes, as in the \"ansi\" subset of SQL")]
        public static string GetSchemaIdentifier(string schemaName) => QuoteIdentifier(schemaName);

        /// <summary>
        /// Gets the SQL Server data type used for the specified CLR type (Nullable types use their underlying type).
        /// DateTime, DateTimeOffset and TimeSpan are stored as bigint nanoseconds, bool as bit, Guid as uniqueidentifier,
        /// and strings, enums and other types as varchar(255)
        /// </summary>
        [Obsolete("Use GetColumnType, which works on every database")]
        public static string GetColumnDataType(Type input)
        {
            var type = Nullable.GetUnderlyingType(input) ?? input;

            if (type == typeof(DateTime)) return "bigint";
            if (type == typeof(DateTimeOffset)) return "bigint";
            if (type == typeof(TimeSpan)) return "bigint";
            if (type == typeof(bool)) return "bit";
            if (type == typeof(byte)) return "tinyint";
            if (type == typeof(sbyte)) return "smallint";
            if (type == typeof(short)) return "smallint";
            if (type == typeof(ushort)) return "int";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "bigint";
            if (type == typeof(long)) return "bigint";
            if (type == typeof(ulong)) return "decimal(20, 0)";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "float";
            if (type == typeof(decimal)) return "decimal(38, 10)";
            if (type == typeof(Guid)) return "uniqueidentifier";
            if (type == typeof(char)) return "nchar(1)";

            return "varchar(255)";
        }

        /// <summary>
        /// Splits a script into Statements on GO batch separator lines (null if the script is null or empty).
        /// Lines inside strings, bracketed or quoted identifiers and block comments are never separators
        /// </summary>
        [Obsolete("GO only exists on SQL Server. Use a Query step for each Statement")]
        public static IEnumerable<string> SplitQueryStatements(string query)
        {
            if (string.IsNullOrEmpty(query)) return null;

            var statements = new List<string>();
            var statement = new StringBuilder();
            var state = ScanState.Code;
            var commentDepth = 0;

            using (var reader = new StringReader(query))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (state == ScanState.Code && IsBatchSeparator(line))
                    {
                        AddStatement(statements, statement);
                    }
                    else
                    {
                        ScanLine(line, ref state, ref commentDepth);
                        statement.Append(line).Append(_newLine);
                    }
                }
            }

            AddStatement(statements, statement);

            return statements;
        }


        /// <summary>
        /// Gets the Data Type of a SQL Server data type, ignoring any length or precision (null if it isn't set, and String for types that aren't numbers)
        /// </summary>
        internal static TrakHoundTableDataType? GetSqlServerColumnType(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType)) return null;

            switch (dataType.Split('(')[0].Trim().ToLowerInvariant())
            {
                case "tinyint": return TrakHoundTableDataType.Byte;
                case "smallint": return TrakHoundTableDataType.Int16;
                case "int": return TrakHoundTableDataType.Int32;
                case "bigint": return TrakHoundTableDataType.Int64;
                case "bit": return TrakHoundTableDataType.Boolean;
                case "float":
                case "real": return TrakHoundTableDataType.Float;
                case "decimal":
                case "numeric": return TrakHoundTableDataType.Decimal;
                case "uniqueidentifier": return TrakHoundTableDataType.Guid;
                case "binary":
                case "varbinary": return TrakHoundTableDataType.Binary;
                default: return TrakHoundTableDataType.String;
            }
        }

        /// <summary>
        /// Gets the number of Columns of the Table for T (the number of Parameters per row of a parameterized Insert or Merge)
        /// </summary>
        internal static int GetColumnCount<T>() => GetSqlServerDefinition(typeof(T)).Columns.Length;

        private static TableDefinition GetSqlServerDefinition(Type type) => _definitions.GetOrAdd(type, CreateSqlServerDefinition);

        private static TableDefinition CreateSqlServerDefinition(Type type)
        {
            var table = GetTable(type);
            var columns = table.Properties.Select(CreateSqlServerColumn).ToArray();

            return new TableDefinition
            {
                Name = table.Definition.Name,
                Schema = table.Definition.Schema,
                IgnoreDuplicateKey = table.Definition.IgnoreDuplicateKey,
                Columns = columns,
                PrimaryKeys = columns.Where(o => o.IsPrimaryKey).ToArray(),
                PrimaryKeyIndexes = Enumerable.Range(0, columns.Length).Where(i => columns[i].IsPrimaryKey).ToArray(),
                Indexes = table.Definition.Indexes
            };
        }

        private static string GetIndexColumn(TrakHoundTableIndexColumn column)
        {
            if (column.Order == TrakHoundSortOrder.Ascending) return $"{QuoteIdentifier(column.Name)} asc";
            if (column.Order == TrakHoundSortOrder.Descending) return $"{QuoteIdentifier(column.Name)} desc";
            return QuoteIdentifier(column.Name);
        }

        // The values of each Column of the model, as literals or (when parameters is set) as references to new Parameters
        private static string[] GetValues(TableDefinition definition, object model, TrakHoundTableParameterCollection parameters)
        {
            var values = new string[definition.Columns.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var column = definition.Columns[i];
                values[i] = parameters != null ? AddParameter(parameters, column, model) : column.GetValue(model);
            }
            return values;
        }

        // Parameters are named by their position (p0, p1, ...) so the same shape of script has the same Query text
        private static string AddParameter(TrakHoundTableParameterCollection parameters, TableColumn column, object model)
        {
            var value = GetColumnValue(column.Property.PropertyType, column.Property.GetValue(model));
            switch (value)
            {
                case DBNull: value = null; break; // Sent as DBNull by the Service (and null serializes to JSON)
                case double d when !double.IsFinite(d): throw new ArgumentException($"{d} can't be stored in a SQL Column");
                case float f when !float.IsFinite(f): throw new ArgumentException($"{f} can't be stored in a SQL Column");
                case ulong u: value = (decimal)u; break; // SqlParameter has no mapping for ulong
            }

            var name = $"p{parameters.Count}";
            parameters.Add(name, column.Definition, value);
            return $"@{name}";
        }

        private static TableColumn CreateSqlServerColumn(PropertyInfo property)
        {
            var name = GetColumnName(property);
            var column = CreateColumn(property);

            return new TableColumn
            {
                Property = property,
                Name = name,
                Identifier = QuoteIdentifier(name),
                Definition = GetSqlServerColumnDefinition(property, column),
                AllowNull = column.AllowNull,
                IsPrimaryKey = column.IsPrimaryKey,
                Order = column.PrimaryKeyOrder,
                IsUpdate = column.IsUpdate
            };
        }

        // A model can set the newer Length and Unicode as well, so these scripts create the same Column as a Table Request
        private static string GetSqlServerColumnDefinition(PropertyInfo property, TrakHoundTableColumnDefinition column)
        {
            var attribute = property.GetCustomAttribute<ColumnAttribute>();
            if (!string.IsNullOrEmpty(attribute?.Definition)) return attribute.Definition;

            if (attribute != null && column.DataType == TrakHoundTableDataType.String && !column.FixedLength && (attribute.Length != 0 || attribute.Unicode))
            {
                var length = column.Length == TrakHoundTableColumnDefinition.MaxLength ? "max" : column.Length.ToString();
                return $"{(column.Unicode ? "nvarchar" : "varchar")}({length})";
            }

            return GetColumnDataType(property.PropertyType);
        }

        // Table Variables (ex. @items) are used as written
        private static string GetTarget(TableDefinition definition, string tableName, string schemaName)
        {
            var name = !string.IsNullOrEmpty(tableName) ? tableName : definition.Name;
            if (name.StartsWith('@')) return name;

            return GetTableIdentifier(name, !string.IsNullOrEmpty(schemaName) ? schemaName : definition.Schema);
        }

        private static string GetPrimaryKeyColumn(TableColumn column)
        {
            if (column.Order == TrakHoundSortOrder.Ascending) return $"{column.Identifier} asc";
            if (column.Order == TrakHoundSortOrder.Descending) return $"{column.Identifier} desc";
            return column.Identifier;
        }

        private static string QuoteIdentifier(string name) => $"[{name?.Replace("]", "]]")}]";

        // N prefix so Unicode characters are kept in nvarchar Columns
        private static string QuoteString(string value) => $"N'{value.Replace("'", "''")}'";

        /// <summary>
        /// Formats a Column value (see GetColumnValue) as a SQL literal. Numbers use the invariant culture so the
        /// script does not depend on the server's culture
        /// </summary>
        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "null";
                case DBNull: return "null";
                case string s: return QuoteString(s);
                case double d when !double.IsFinite(d): throw new ArgumentException($"{d} can't be stored in a SQL Column");
                case float f when !float.IsFinite(f): throw new ArgumentException($"{f} can't be stored in a SQL Column");
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                    return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
                case IFormattable formattable: return QuoteString(formattable.ToString(null, CultureInfo.InvariantCulture));
                default: return QuoteString(value.ToString());
            }
        }


        private enum ScanState
        {
            Code,
            String,
            BracketIdentifier,
            QuotedIdentifier,
            BlockComment
        }

        // Tracks whether the next line starts inside a string, identifier or block comment
        private static void ScanLine(string line, ref ScanState state, ref int commentDepth)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';

                switch (state)
                {
                    case ScanState.Code:
                        if (c == '\'') state = ScanState.String;
                        else if (c == '[') state = ScanState.BracketIdentifier;
                        else if (c == '"') state = ScanState.QuotedIdentifier;
                        else if (c == '-' && next == '-') return; // Rest of the line is a comment
                        else if (c == '/' && next == '*') { state = ScanState.BlockComment; commentDepth = 1; i++; }
                        break;

                    case ScanState.String:
                        if (c == '\'') { if (next == '\'') i++; else state = ScanState.Code; }
                        break;

                    case ScanState.BracketIdentifier:
                        if (c == ']') { if (next == ']') i++; else state = ScanState.Code; }
                        break;

                    case ScanState.QuotedIdentifier:
                        if (c == '"') { if (next == '"') i++; else state = ScanState.Code; }
                        break;

                    case ScanState.BlockComment:
                        // Block comments nest in T-SQL
                        if (c == '/' && next == '*') { commentDepth++; i++; }
                        else if (c == '*' && next == '/') { commentDepth--; i++; if (commentDepth == 0) state = ScanState.Code; }
                        break;
                }
            }
        }

        private static bool IsBatchSeparator(string line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase)) return false;
            if (trimmed.Length == 2) return true;

            var next = trimmed[2];
            return next == ';' || char.IsWhiteSpace(next); // "GO;", "GO 5", "GO -- comment"
        }

        private static void AddStatement(List<string> statements, StringBuilder statement)
        {
            if (statement.Length > 0)
            {
                var text = statement.ToString();
                if (!string.IsNullOrWhiteSpace(text)) statements.Add(text);
                statement.Clear();
            }
        }
    }
}
