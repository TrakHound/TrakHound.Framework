// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Generates the SQL a database needs for the typed steps of a Table Request. The default implementations use standard
    /// create table and "insert ... on conflict" statements, which a Dialect overrides where its database differs
    /// </summary>
    public abstract class TrakHoundSqlDialect
    {
        /// <summary>
        /// The name of this Dialect (ex. sqlserver)
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// The Query Variants the database runs, in order of preference (ex. sqlserver, ansi)
        /// </summary>
        public abstract string[] SupportedDialects { get; }

        /// <summary>
        /// The most rows written by one Statement
        /// </summary>
        public virtual int MaxRowsPerStatement => 1000;

        /// <summary>
        /// The most Parameters sent with one Statement
        /// </summary>
        public abstract int MaxParameters { get; }


        /// <summary>
        /// Gets the database type of a Column
        /// </summary>
        protected abstract string GetColumnType(TrakHoundTableColumnDefinition column);


        /// <summary>
        /// Quotes an identifier (ex. a Table or Column name)
        /// </summary>
        public virtual string QuoteIdentifier(string name) => $"\"{name?.Replace("\"", "\"\"")}\"";

        /// <summary>
        /// Gets the quoted Table name, including its Schema when set
        /// </summary>
        public virtual string GetTableIdentifier(string tableName, string schemaName = null)
        {
            return !string.IsNullOrEmpty(schemaName) ? $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}" : QuoteIdentifier(tableName);
        }

        /// <summary>
        /// Splits a script into the batches that are each run as a command
        /// </summary>
        public virtual IEnumerable<string> SplitScript(string script)
        {
            return !string.IsNullOrWhiteSpace(script) ? new[] { script } : null;
        }

        /// <summary>
        /// Adds paging to a Query, which should have an order by so the pages are stable
        /// </summary>
        public virtual string AddPaging(string query, int? skip, int? take)
        {
            if (!skip.HasValue && !take.HasValue) return query;

            var text = TrimQuery(query);
            if (take.HasValue) text += $" limit {take.Value}";
            if (skip.HasValue) text += $" offset {skip.Value}";
            return text + ";";
        }

        /// <summary>
        /// Generates the Statements that create the Schema, Table and Indexes if they don't exist
        /// </summary>
        public virtual IEnumerable<TrakHoundSqlStatement> GenerateEnsureTable(TrakHoundTableDefinition definition)
        {
            var statements = new List<TrakHoundSqlStatement>();
            var table = GetTableIdentifier(definition.Name, definition.Schema);

            var createSchema = GenerateCreateSchema(definition.Schema);
            if (createSchema != null) statements.Add(new TrakHoundSqlStatement(createSchema));

            var parts = definition.Columns.Select(o => $"{QuoteIdentifier(o.Name)} {GetColumnType(o)}{(o.AllowNull ? "" : " not null")}").ToList();
            var keys = GetPrimaryKeys(definition);
            if (keys.Length > 0) parts.Add($"primary key ({string.Join(", ", keys.Select(o => QuoteIdentifier(o.Name)))})");

            statements.Add(new TrakHoundSqlStatement($"create table if not exists {table} ({string.Join(", ", parts)});"));

            if (definition.Indexes != null)
            {
                foreach (var index in definition.Indexes)
                {
                    statements.Add(new TrakHoundSqlStatement(GenerateCreateIndex(definition, index)));
                }
            }

            return statements;
        }

        /// <summary>
        /// Generates the Statements that write rows (delete existing rows with the same Primary Key first when overwrite is true)
        /// </summary>
        public virtual IEnumerable<TrakHoundSqlStatement> GenerateInsert(TrakHoundTableDefinition definition, object[][] rows, bool overwrite)
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

                var conflict = definition.IgnoreDuplicateKey ? " on conflict do nothing" : "";
                statements.Add(new TrakHoundSqlStatement($"insert into {table} ({columns}) values {FormatRows(rowValues)}{conflict};", parameters));
            }

            return statements;
        }

        /// <summary>
        /// Generates the Statements that update existing rows (matched by Primary Key) and insert new rows. Throws an
        /// InvalidOperationException if the Table has no Primary Key
        /// </summary>
        public virtual IEnumerable<TrakHoundSqlStatement> GenerateMerge(TrakHoundTableDefinition definition, object[][] rows)
        {
            var keys = GetPrimaryKeys(definition);
            if (keys.Length == 0) throw new InvalidOperationException($"Cannot Merge into {definition.Name}: it has no Primary Key");

            var statements = new List<TrakHoundSqlStatement>();
            var table = GetTableIdentifier(definition.Name, definition.Schema);
            var columns = string.Join(", ", definition.Columns.Select(o => QuoteIdentifier(o.Name)));
            var keyColumns = string.Join(", ", keys.Select(o => QuoteIdentifier(o.Name)));

            var updateColumns = definition.Columns.Where(o => !o.IsPrimaryKey).ToArray();
            var changeColumns = updateColumns.Where(o => o.IsUpdate).ToArray();

            string action;
            if (updateColumns.Length > 0)
            {
                action = $"do update set {string.Join(", ", updateColumns.Select(o => $"{QuoteIdentifier(o.Name)} = excluded.{QuoteIdentifier(o.Name)}"))}";

                // Only update when an Update Column changed ("is distinct from" compares nulls as equal)
                if (changeColumns.Length > 0)
                {
                    action += $" where {string.Join(" or ", changeColumns.Select(o => $"{table}.{QuoteIdentifier(o.Name)} is distinct from excluded.{QuoteIdentifier(o.Name)}"))}";
                }
            }
            else
            {
                action = "do nothing";
            }

            foreach (var batch in ChunkRows(definition, rows))
            {
                var parameters = new TrakHoundTableParameterCollection();
                var rowValues = AddParameters(definition, batch, parameters);

                statements.Add(new TrakHoundSqlStatement($"insert into {table} ({columns}) values {FormatRows(rowValues)} on conflict ({keyColumns}) {action};", parameters));
            }

            return statements;
        }

        /// <summary>
        /// Generates a Statement that deletes up to batchSize rows matching the condition, which uses "target" as the name of the Table
        /// </summary>
        public abstract string GenerateDeleteBatch(string tableName, string schemaName, string where, int batchSize);


        /// <summary>
        /// Generates the Statement that creates a Schema if it doesn't exist (null if the database doesn't need one)
        /// </summary>
        protected virtual string GenerateCreateSchema(string schemaName)
        {
            return !string.IsNullOrEmpty(schemaName) ? $"create schema if not exists {QuoteIdentifier(schemaName)};" : null;
        }

        /// <summary>
        /// Generates the Statement that creates an Index if it doesn't exist. Clustered and Include are ignored by default
        /// </summary>
        protected virtual string GenerateCreateIndex(TrakHoundTableDefinition definition, TrakHoundTableIndex index)
        {
            var unique = index.IsUnique ? "unique " : "";
            return $"create {unique}index if not exists {QuoteIdentifier(index.Name)} on {GetTableIdentifier(definition.Name, definition.Schema)} ({FormatIndexColumns(index)});";
        }

        /// <summary>
        /// Generates the Statement that deletes the rows with the specified Primary Keys
        /// </summary>
        protected virtual string GenerateDeleteByKey(TrakHoundTableDefinition definition, string table, string[][] rowValues, int[] keyIndexes)
        {
            var keyColumns = string.Join(", ", keyIndexes.Select(i => QuoteIdentifier(definition.Columns[i].Name)));
            var keyRows = string.Join(", ", rowValues.Select(values => $"({string.Join(", ", keyIndexes.Select(i => values[i]))})"));
            return $"delete from {table} where ({keyColumns}) in (values {keyRows});";
        }

        /// <summary>
        /// Formats the key Columns of an Index
        /// </summary>
        protected string FormatIndexColumns(TrakHoundTableIndex index)
        {
            return string.Join(", ", index.Columns.Select(o =>
            {
                if (o.Order == TrakHoundSortOrder.Ascending) return $"{QuoteIdentifier(o.Name)} asc";
                if (o.Order == TrakHoundSortOrder.Descending) return $"{QuoteIdentifier(o.Name)} desc";
                return QuoteIdentifier(o.Name);
            }));
        }

        /// <summary>
        /// Splits the rows so each Statement stays within the limits of the database
        /// </summary>
        protected IEnumerable<object[][]> ChunkRows(TrakHoundTableDefinition definition, object[][] rows)
        {
            var maxRows = Math.Max(1, Math.Min(MaxRowsPerStatement, MaxParameters / Math.Max(1, definition.Columns.Length)));
            return rows.Chunk(maxRows);
        }

        /// <summary>
        /// Adds the values of the rows as Parameters, and returns the Parameter reference of each value (in the same layout as the rows)
        /// </summary>
        protected static string[][] AddParameters(TrakHoundTableDefinition definition, object[][] rows, TrakHoundTableParameterCollection parameters)
        {
            var result = new string[rows.Length][];
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null || rows[i].Length != definition.Columns.Length)
                {
                    throw new ArgumentException($"Each row of {definition.Name} needs a value for each of its {definition.Columns.Length} Columns");
                }

                result[i] = new string[rows[i].Length];
                for (var j = 0; j < rows[i].Length; j++)
                {
                    var name = $"p{parameters.Count}";
                    parameters.Add(name, definition.Columns[j].DataType, rows[i][j]);
                    result[i][j] = $"@{name}";
                }
            }
            return result;
        }

        /// <summary>
        /// Formats rows of Parameter references as the rows of a values list
        /// </summary>
        protected static string FormatRows(string[][] rowValues)
        {
            return string.Join(", ", rowValues.Select(values => $"({string.Join(", ", values)})"));
        }

        /// <summary>
        /// Gets the Primary Key Columns of a Table
        /// </summary>
        protected static TrakHoundTableColumnDefinition[] GetPrimaryKeys(TrakHoundTableDefinition definition)
        {
            return definition.Columns.Where(o => o.IsPrimaryKey).ToArray();
        }

        /// <summary>
        /// Gets the positions of the Primary Key Columns of a Table
        /// </summary>
        protected static int[] GetPrimaryKeyIndexes(TrakHoundTableDefinition definition)
        {
            return Enumerable.Range(0, definition.Columns.Length).Where(i => definition.Columns[i].IsPrimaryKey).ToArray();
        }

        /// <summary>
        /// Removes trailing whitespace and semicolons from a Query
        /// </summary>
        protected static string TrimQuery(string query) => query?.TrimEnd(' ', '\t', '\r', '\n', ';');
    }
}
