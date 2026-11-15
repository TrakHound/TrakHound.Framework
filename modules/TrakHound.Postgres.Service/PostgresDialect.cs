// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables;

namespace TrakHound.Postgres.Service
{
    /// <summary>
    /// Generates PostgreSQL SQL for the steps of a Table Request
    /// </summary>
    public class PostgresDialect : TrakHoundSqlDialect
    {
        public override string Name => TrakHoundTableDialects.Postgres;

        public override string[] SupportedDialects { get; } = new[] { TrakHoundTableDialects.Postgres, TrakHoundTableDialects.Ansi };

        // PostgreSQL allows 65535 Parameters per statement
        public override int MaxParameters => 30000;


        protected override string GetColumnType(TrakHoundTableColumnDefinition column)
        {
            switch (column.DataType)
            {
                case TrakHoundTableDataType.Int64: return "bigint";
                case TrakHoundTableDataType.Boolean: return "boolean";
                case TrakHoundTableDataType.Byte: return "smallint"; // No 8-bit type
                case TrakHoundTableDataType.Int16: return "smallint";
                case TrakHoundTableDataType.Int32: return "integer";
                case TrakHoundTableDataType.Float: return "double precision";
                case TrakHoundTableDataType.Guid: return "uuid";
                case TrakHoundTableDataType.Decimal: return $"numeric({column.Precision ?? 38}, {column.Scale ?? 10})";
                case TrakHoundTableDataType.Timestamp: return "timestamptz";
                case TrakHoundTableDataType.Duration: return "interval";
                case TrakHoundTableDataType.Binary: return "bytea";
            }

            if (column.Length == TrakHoundTableColumnDefinition.MaxLength) return "text";

            var length = column.Length ?? 255;
            return column.FixedLength ? $"char({length})" : $"varchar({length})";
        }

        protected override string GenerateCreateIndex(TrakHoundTableDefinition definition, TrakHoundTableIndex index)
        {
            var unique = index.IsUnique ? "unique " : "";
            var include = !index.IncludeColumns.IsNullOrEmpty() ? $" include ({string.Join(", ", index.IncludeColumns.Select(QuoteIdentifier))})" : "";

            return $"create {unique}index if not exists {QuoteIdentifier(index.Name)} on {GetTableIdentifier(definition.Name, definition.Schema)} ({FormatIndexColumns(index)}){include};";
        }

        public override string GenerateDeleteBatch(string tableName, string schemaName, string where, int batchSize)
        {
            var table = GetTableIdentifier(tableName, schemaName);
            return $"delete from {table} where ctid in (select \"target\".ctid from {table} as \"target\" where {where} limit {batchSize});";
        }
    }
}
