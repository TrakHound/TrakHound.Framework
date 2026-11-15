// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Builds a SQL script from Statements, splitting it into batches (separated by GO) of up to the Batch Size lines,
    /// optionally wrapping each batch in a Transaction
    /// </summary>
    [Obsolete("Generates SQL that only runs on SQL Server. Use TrakHoundTableRequestBuilder")]
    public class TrakHoundTableQueryBuilder
    {
        private const int _defaultBatchSize = 1000;
        private const string _go = "GO";
        private const string _newLine = "\r\n";

        // SQL Server allows 2100 Parameters per command, and every batch of the script is sent with all of the Parameters,
        // so the whole script stays under the limit (leaving room for Parameters the caller adds)
        private const int _maxParameters = 2000;

        private readonly List<Statement> _statements = new List<Statement>();
        private readonly int _batchSize;


        struct Statement
        {
            public string Text;
            public int LineCount;
            public bool Partial;
        }


        /// <summary>
        /// The Parameters referenced by the script (never null, empty unless UseParameters added values or the caller added its own).
        /// Pass them with the Query, ex. ExecuteNonQuery(builder.Build(), parameters: builder.Parameters)
        /// </summary>
        public TrakHoundTableParameterCollection Parameters { get; } = new TrakHoundTableParameterCollection();

        /// <summary>
        /// When true, AppendInsert and AppendMerge add the item values to Parameters (referenced as @p0, @p1, ...) instead of writing
        /// literals, so repeated Inserts of the same shape reuse their query plan. A batch whose values would take the script over
        /// 2000 Parameters is written with literals. Off by default since the script then only runs with the Parameters passed
        /// </summary>
        public bool UseParameters { get; set; }


        /// <summary>
        /// Creates a Query Builder with the specified Batch Size (the number of lines or rows per batch)
        /// </summary>
        public TrakHoundTableQueryBuilder(int batchSize = _defaultBatchSize)
        {
            _batchSize = batchSize;
        }


        /// <summary>
        /// Appends a script that is run in its own batch, unmodified and in the order it was appended
        /// </summary>
        public void AppendRaw(string query)
        {
            if (!string.IsNullOrEmpty(query))
            {
                var statement = new Statement();
                statement.Text = query;
                statement.LineCount = CountLines(query);
                statement.Partial = false;
                _statements.Add(statement);
            }
        }

        /// <summary>
        /// Appends a Statement (a trailing ';' is added if missing)
        /// </summary>
        public void Append(string query)
        {
            if (!string.IsNullOrEmpty(query))
            {
                var text = query.Trim();
                if (!text.EndsWith(';')) text += ';';

                var statement = new Statement();
                statement.Text = text;
                statement.LineCount = CountLines(query);
                statement.Partial = true;
                _statements.Add(statement);
            }
        }

        /// <summary>
        /// Appends each of the specified Statements
        /// </summary>
        public void Append(IEnumerable<string> queries)
        {
            if (!queries.IsNullOrEmpty())
            {
                foreach (var query in queries)
                {
                    Append(query);
                }
            }
        }

        /// <summary>
        /// Appends an empty line
        /// </summary>
        public void AppendLine()
        {
            var statement = new Statement();
            statement.Text = _newLine;
            statement.LineCount = 1;
            statement.Partial = true;
            _statements.Add(statement);
        }

        /// <summary>
        /// Appends a Statement that creates the Schema and Table for TItem if they do not exist
        /// </summary>
        public void AppendCreateTable<TItem>()
        {
            Append(TrakHoundTables.GenerateCreate<TItem>());
            AppendLine();
        }

        /// <summary>
        /// Appends Insert Statements for the specified items (in batches of up to the Batch Size rows)
        /// </summary>
        public void AppendInsert<TItem>(IEnumerable<TItem> items)
        {
            AppendInsert(items, false);
        }

        /// <summary>
        /// Appends Insert Statements for the specified items (in batches of up to the Batch Size rows).
        /// When overwrite is true, existing rows with the same Primary Key are deleted first
        /// </summary>
        public void AppendInsert<TItem>(IEnumerable<TItem> items, bool overwrite)
        {
            AppendBatches(items, (batchItems, parameters) => TrakHoundTables.GenerateInsert(batchItems, parameters, overwrite: overwrite));
        }

        /// <summary>
        /// Appends Merge Statements for the specified items (in batches of up to the Batch Size rows)
        /// </summary>
        public void AppendMerge<TItem>(IEnumerable<TItem> items)
        {
            AppendBatches(items, (batchItems, parameters) => TrakHoundTables.GenerateMerge(batchItems, parameters));
        }

        /// <summary>
        /// Appends the contents of the specified script file to run in its own batch
        /// </summary>
        public void AppendScriptFromFile(string filePath)
        {
            AppendRaw(TrakHoundTables.ReadScriptFromFile(filePath));
            AppendLine();
        }

        private void AppendBatches<TItem>(IEnumerable<TItem> items, Func<TItem[], TrakHoundTableParameterCollection, string> generate)
        {
            if (!items.IsNullOrEmpty())
            {
                var columnCount = UseParameters ? TrakHoundTables.GetColumnCount<TItem>() : 0;

                foreach (var batchItems in items.Chunk(_batchSize))
                {
                    // Use Parameters only when the whole batch fits under the limit (null writes literals)
                    var parameters = UseParameters && Parameters.Count + (batchItems.Length * columnCount) <= _maxParameters ? Parameters : null;

                    var statement = new Statement();
                    statement.Text = generate(batchItems, parameters);
                    statement.LineCount = batchItems.Length;
                    statement.Partial = true;
                    _statements.Add(statement);
                }
            }

            AppendLine();
        }

        private static int CountLines(string text)
        {
            var count = 1;
            foreach (var c in text)
            {
                if (c == '\n') count++;
            }
            return count;
        }


        /// <summary>
        /// Builds the script (null if nothing was appended). When useTransactions is true, each batch is run in a Transaction
        /// that is rolled back on any error
        /// </summary>
        public string Build(bool useTransactions = true)
        {
            if (_statements.IsNullOrEmpty()) return null;

            var batches = new List<string>();
            StringBuilder batch = null;
            var lineCount = 0;

            void BeginBatch()
            {
                batch = new StringBuilder();

                if (useTransactions)
                {
                    // Roll back the entire Transaction on any error
                    batch.AppendLine("set xact_abort on;");
                    batch.AppendLine("begin transaction;");
                    batch.AppendLine();
                }
            }

            void EndBatch()
            {
                if (batch == null) return;

                if (useTransactions)
                {
                    batch.AppendLine();
                    batch.AppendLine("commit transaction;");
                }

                batches.Add(batch.ToString().Trim());
                batch = null;
                lineCount = 0;
            }

            foreach (var statement in _statements)
            {
                if (!statement.Partial)
                {
                    // Raw scripts run in their own batch, in the order they were appended
                    EndBatch();

                    var text = statement.Text.Trim();
                    if (!string.IsNullOrEmpty(text)) batches.Add(text);
                    continue;
                }

                var substatements = TrakHoundTables.SplitQueryStatements(statement.Text);
                if (substatements.IsNullOrEmpty()) continue;

                var counted = false;
                foreach (var substatement in substatements)
                {
                    if (batch == null) BeginBatch();

                    batch.AppendLine(substatement.Trim());

                    // Count the Statement's lines once (not once per substatement)
                    if (!counted)
                    {
                        lineCount += statement.LineCount;
                        counted = true;
                    }

                    if (lineCount >= _batchSize) EndBatch();
                }
            }

            // Always close the last batch
            EndBatch();

            // GO only separates batches (a single batch has none, so it runs anywhere without splitting on GO)
            return string.Join($"{_newLine}{_newLine}{_go}{_newLine}{_newLine}", batches);
        }
    }
}
