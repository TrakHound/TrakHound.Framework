// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using System.Text;
using TrakHound.Services.Tables;

namespace TrakHound.SqlServer.Service
{
    /// <summary>
    /// Generates T-SQL for the steps of a Table Request
    /// </summary>
    public class SqlServerDialect : TrakHoundSqlDialect
    {
        private const string _newLine = "\r\n";


        public override string Name => TrakHoundTableDialects.SqlServer;

        public override string[] SupportedDialects { get; } = new[] { TrakHoundTableDialects.SqlServer, TrakHoundTableDialects.Ansi };

        // SQL Server allows 2100 Parameters per command
        public override int MaxParameters => 2000;


        public override string QuoteIdentifier(string name) => $"[{name?.Replace("]", "]]")}]";

        public override IEnumerable<string> SplitScript(string script) => SplitQueryStatements(script);

        public override string AddPaging(string query, int? skip, int? take)
        {
            if (!skip.HasValue && !take.HasValue) return query;

            var text = $"{TrimQuery(query)} offset {skip ?? 0} rows";
            if (take.HasValue) text += $" fetch next {take.Value} rows only";
            return text + ";";
        }

        protected override string GetColumnType(TrakHoundTableColumnDefinition column)
        {
            switch (column.DataType)
            {
                case TrakHoundTableDataType.Int64: return "bigint";
                case TrakHoundTableDataType.Boolean: return "bit";
                case TrakHoundTableDataType.Byte: return "tinyint";
                case TrakHoundTableDataType.Int16: return "smallint";
                case TrakHoundTableDataType.Int32: return "int";
                case TrakHoundTableDataType.Float: return "float";
                case TrakHoundTableDataType.Guid: return "uniqueidentifier";
                case TrakHoundTableDataType.Decimal: return $"decimal({column.Precision ?? 38}, {column.Scale ?? 10})";
                case TrakHoundTableDataType.Timestamp: return "datetime2";
                case TrakHoundTableDataType.Duration: return "time";
                case TrakHoundTableDataType.Binary: return column.Length.HasValue && column.Length > 0 ? $"varbinary({column.Length})" : "varbinary(max)";
            }

            var type = column.FixedLength ? "char" : "varchar";
            if (column.Unicode) type = "n" + type;
            return column.Length == TrakHoundTableColumnDefinition.MaxLength ? $"{type}(max)" : $"{type}({column.Length ?? 255})";
        }

        // One script, since "if not exists" with begin/end has to be in one batch. Indexes are checked separately from the Table so they are added to existing Tables
        public override IEnumerable<TrakHoundSqlStatement> GenerateEnsureTable(TrakHoundTableDefinition definition)
        {
            var tableIdentifier = GetTableIdentifier(definition.Name, definition.Schema);
            var columns = definition.Columns.Select(o => $"{QuoteIdentifier(o.Name)} {GetColumnType(o)}{(o.AllowNull ? "" : " NOT NULL")}");

            var queryLines = new List<string>();

            if (!string.IsNullOrEmpty(definition.Schema))
            {
                queryLines.Add($"if not exists (select * from sys.schemas where [name] = {QuoteString(definition.Schema)})");
                queryLines.Add("begin");
                queryLines.Add($"   EXEC({QuoteString($"create schema {QuoteIdentifier(definition.Schema)};")});");
                queryLines.Add("end;");
            }

            var schemaId = !string.IsNullOrEmpty(definition.Schema) ? $"SCHEMA_ID({QuoteString(definition.Schema)})" : "SCHEMA_ID()";
            queryLines.Add($"if not exists (select * from sys.tables where [schema_id] = {schemaId} and [name] = {QuoteString(definition.Name)})");
            queryLines.Add("begin");
            queryLines.Add($"   create table {tableIdentifier} ({string.Join(", ", columns)});");

            var keys = GetPrimaryKeys(definition);
            if (keys.Length > 0)
            {
                var primaryKeyColumns = string.Join(", ", keys.Select(GetPrimaryKeyColumn));
                var options = definition.IgnoreDuplicateKey ? " with (IGNORE_DUP_KEY = ON)" : "";

                queryLines.Add($"   alter table {tableIdentifier} add constraint {QuoteIdentifier($"{definition.Name}_primary_key")} primary key clustered ({primaryKeyColumns}){options};");
            }

            queryLines.Add("end;");

            if (definition.Indexes != null)
            {
                foreach (var index in definition.Indexes)
                {
                    var indexType = (index.IsUnique ? "unique " : "") + (index.Type == TrakHoundTableIndexType.Clustered ? "clustered" : "nonclustered");
                    var include = !index.IncludeColumns.IsNullOrEmpty() ? $" include ({string.Join(", ", index.IncludeColumns.Select(QuoteIdentifier))})" : "";

                    queryLines.Add($"if not exists (select * from sys.indexes where [object_id] = OBJECT_ID({QuoteString(tableIdentifier)}) and [name] = {QuoteString(index.Name)})");
                    queryLines.Add("begin");
                    queryLines.Add($"   create {indexType} index {QuoteIdentifier(index.Name)} on {tableIdentifier} ({FormatIndexColumns(index)}){include};");
                    queryLines.Add("end;");
                }
            }

            return new[] { new TrakHoundSqlStatement(string.Join(_newLine, queryLines)) };
        }

        public override IEnumerable<TrakHoundSqlStatement> GenerateInsert(TrakHoundTableDefinition definition, object[][] rows, bool overwrite)
        {
            var statements = new List<TrakHoundSqlStatement>();
            var table = GetTableIdentifier(definition.Name, definition.Schema);
            var columns = string.Join(", ", definition.Columns.Select(o => QuoteIdentifier(o.Name)));
            var keyIndexes = GetPrimaryKeyIndexes(definition);

            foreach (var batch in ChunkRows(definition, rows))
            {
                var parameters = new TrakHoundTableParameterCollection();
                var rowValues = AddParameters(definition, batch, parameters);

                if (overwrite && keyIndexes.Length > 0)
                {
                    statements.Add(new TrakHoundSqlStatement(GenerateDeleteByKey(definition, table, rowValues, keyIndexes), parameters));
                }

                // Duplicate Keys are skipped by the Table's IGNORE_DUP_KEY setting
                statements.Add(new TrakHoundSqlStatement($"insert into {table} ({columns}) values {FormatRows(rowValues)};", parameters));
            }

            return statements;
        }

        protected override string GenerateDeleteByKey(TrakHoundTableDefinition definition, string table, string[][] rowValues, int[] keyIndexes)
        {
            var keyColumns = string.Join(", ", keyIndexes.Select(i => QuoteIdentifier(definition.Columns[i].Name)));
            var keyRows = string.Join(", ", rowValues.Select(values => $"({string.Join(", ", keyIndexes.Select(i => values[i]))})"));
            var keyConditions = string.Join(" and ", keyIndexes.Select(i => $"[input].{QuoteIdentifier(definition.Columns[i].Name)} = [target].{QuoteIdentifier(definition.Columns[i].Name)}"));

            return $"delete [target] from {table} as [target] inner join (values {keyRows}) as [input] ({keyColumns}) on {keyConditions};";
        }

        public override IEnumerable<TrakHoundSqlStatement> GenerateMerge(TrakHoundTableDefinition definition, object[][] rows)
        {
            var keys = GetPrimaryKeys(definition);
            if (keys.Length == 0) throw new InvalidOperationException($"Cannot Merge into {definition.Name}: it has no Primary Key");

            var statements = new List<TrakHoundSqlStatement>();
            var target = GetTableIdentifier(definition.Name, definition.Schema);
            var columns = string.Join(", ", definition.Columns.Select(o => QuoteIdentifier(o.Name)));

            foreach (var batch in ChunkRows(definition, rows))
            {
                var parameters = new TrakHoundTableParameterCollection();
                var rowValues = AddParameters(definition, batch, parameters);

                var queryBuilder = new StringBuilder();

                // Table Variable with the same Column types as the Table so values are not truncated
                queryBuilder.Append($"declare @merge_input as table ({string.Join(", ", definition.Columns.Select(o => $"{QuoteIdentifier(o.Name)} {GetColumnType(o)}"))});{_newLine}");
                queryBuilder.Append($"insert into @merge_input ({columns}) values {FormatRows(rowValues)};{_newLine}{_newLine}");

                var keyConditions = string.Join(" and ", keys.Select(o => $"[input].{QuoteIdentifier(o.Name)} = [target].{QuoteIdentifier(o.Name)}"));
                queryBuilder.Append($"merge {target} as [target] using @merge_input as [input] on {keyConditions}{_newLine}{_newLine}");

                var updateColumns = definition.Columns.Where(o => !o.IsPrimaryKey).ToArray();
                if (updateColumns.Length > 0)
                {
                    queryBuilder.Append("when matched");

                    // Only update when an Update Column changed ("except" compares nulls as equal, unlike <>)
                    var changeColumns = updateColumns.Where(o => o.IsUpdate).ToArray();
                    if (changeColumns.Length > 0)
                    {
                        var inputColumns = string.Join(", ", changeColumns.Select(o => $"[input].{QuoteIdentifier(o.Name)}"));
                        var targetColumns = string.Join(", ", changeColumns.Select(o => $"[target].{QuoteIdentifier(o.Name)}"));
                        queryBuilder.Append($" and exists (select {inputColumns} except select {targetColumns})");
                    }

                    var updateValues = updateColumns.Select(o => $"[target].{QuoteIdentifier(o.Name)} = [input].{QuoteIdentifier(o.Name)}");
                    queryBuilder.Append($"{_newLine}then update set{_newLine}{string.Join($",{_newLine}", updateValues)}{_newLine}{_newLine}");
                }

                var insertValues = string.Join(", ", definition.Columns.Select(o => $"[input].{QuoteIdentifier(o.Name)}"));
                queryBuilder.Append($"when not matched by target{_newLine}then insert ({columns}){_newLine}values ({insertValues});{_newLine}");

                statements.Add(new TrakHoundSqlStatement(queryBuilder.ToString(), parameters));
            }

            return statements;
        }

        public override string GenerateDeleteBatch(string tableName, string schemaName, string where, int batchSize)
        {
            return $"delete top ({batchSize}) [target] from {GetTableIdentifier(tableName, schemaName)} as [target] where {where};";
        }

        private string GetPrimaryKeyColumn(TrakHoundTableColumnDefinition column)
        {
            if (column.PrimaryKeyOrder == TrakHoundSortOrder.Ascending) return $"{QuoteIdentifier(column.Name)} asc";
            if (column.PrimaryKeyOrder == TrakHoundSortOrder.Descending) return $"{QuoteIdentifier(column.Name)} desc";
            return QuoteIdentifier(column.Name);
        }

        // N prefix so Unicode characters are kept in nvarchar Columns
        private static string QuoteString(string value) => $"N'{value.Replace("'", "''")}'";


        /// <summary>
        /// Splits a script into Statements on GO batch separator lines (null if the script is null or empty).
        /// Lines inside strings, bracketed or quoted identifiers and block comments are never separators
        /// </summary>
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
