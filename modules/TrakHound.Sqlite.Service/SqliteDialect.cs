// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables;

namespace TrakHound.Sqlite.Service
{
    /// <summary>
    /// Generates SQLite SQL for the steps of a Table Request. A Schema is a separate attached database file (see SqliteTableService)
    /// </summary>
    public class SqliteDialect : TrakHoundSqlDialect
    {
        public override string Name => TrakHoundTableDialects.Sqlite;

        public override string[] SupportedDialects { get; } = new[] { TrakHoundTableDialects.Sqlite, TrakHoundTableDialects.Ansi };

        // SQLite allows 32766 Parameters per statement by default
        public override int MaxParameters => 30000;


        // Uses SQLite's storage classes. A Decimal is numeric so it compares as a number, which keeps about 15 significant digits
        protected override string GetColumnType(TrakHoundTableColumnDefinition column)
        {
            switch (column.DataType)
            {
                case TrakHoundTableDataType.Byte:
                case TrakHoundTableDataType.Int16:
                case TrakHoundTableDataType.Int32:
                case TrakHoundTableDataType.Int64:
                case TrakHoundTableDataType.Boolean:
                    return "integer";
                case TrakHoundTableDataType.Float:
                    return "real";
                case TrakHoundTableDataType.Decimal:
                    return "numeric";
                case TrakHoundTableDataType.Binary:
                    return "blob";
                default:
                    return "text";
            }
        }

        // SQLite has no create schema: the file is attached by the Service before the Request runs
        protected override string GenerateCreateSchema(string schemaName) => null;

        // An index is created in the database of its Table, so its name takes the Schema and the Table doesn't
        protected override string GenerateCreateIndex(TrakHoundTableDefinition definition, TrakHoundTableIndex index)
        {
            var unique = index.IsUnique ? "unique " : "";
            var name = GetTableIdentifier(index.Name, definition.Schema);

            return $"create {unique}index if not exists {name} on {QuoteIdentifier(definition.Name)} ({FormatIndexColumns(index)});";
        }

        public override string AddPaging(string query, int? skip, int? take)
        {
            if (!skip.HasValue && !take.HasValue) return query;

            // SQLite needs a limit to use an offset (-1 means no limit)
            return $"{TrimQuery(query)} limit {take ?? -1} offset {skip ?? 0};";
        }

        public override string GenerateDeleteBatch(string tableName, string schemaName, string where, int batchSize)
        {
            var table = GetTableIdentifier(tableName, schemaName);
            return $"delete from {table} where rowid in (select \"target\".rowid from {table} as \"target\" where {where} limit {batchSize});";
        }
    }
}
