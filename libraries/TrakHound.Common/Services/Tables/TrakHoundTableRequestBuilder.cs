// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Builds a Table Request from ordered steps. It records what is asked for and generates no SQL: the Table Service that
    /// receives the Request does that for its own database. Pass the result to Client.Services.Tables.Execute
    /// </summary>
    public class TrakHoundTableRequestBuilder
    {
        private readonly List<TrakHoundTableStep> _steps = new List<TrakHoundTableStep>();


        /// <summary>
        /// The Parameters referenced by the Query steps as @name
        /// </summary>
        public TrakHoundTableParameterCollection Parameters { get; } = new TrakHoundTableParameterCollection();

        /// <summary>
        /// The timeout of each command, in seconds (null uses the Service's default)
        /// </summary>
        public int? Timeout { get; set; }

        /// <summary>
        /// The number of steps that have been added
        /// </summary>
        public int Count => _steps.Count;


        /// <summary>
        /// Adds a step that runs a Query written in the portable "ansi" subset of SQL
        /// </summary>
        public void AppendQuery(string query, int? skip = null, int? take = null)
        {
            AppendQuery(TrakHoundTableQueryVariants.Ansi(query), skip, take);
        }

        /// <summary>
        /// Adds a step that runs the Query written for the Service's Dialect. Skip and Take page the Query, which should have an order by
        /// </summary>
        public void AppendQuery(TrakHoundTableQueryVariants variants, int? skip = null, int? take = null)
        {
            _steps.Add(new TrakHoundTableStep { Type = TrakHoundTableStepType.Query, Variants = variants, Skip = skip, Take = take });
        }

        /// <summary>
        /// Adds a step that creates the Table of TItem and its Indexes if they don't exist
        /// </summary>
        public void AppendEnsureTable<TItem>()
        {
            _steps.Add(new TrakHoundTableStep { Type = TrakHoundTableStepType.EnsureTable, Definition = TrakHoundTables.GetDefinition<TItem>() });
        }

        /// <summary>
        /// Adds a step that writes the items. When overwrite is true, existing rows with the same Primary Key are deleted first
        /// </summary>
        public void AppendInsert<TItem>(IEnumerable<TItem> items, bool overwrite = false)
        {
            if (!items.IsNullOrEmpty())
            {
                _steps.Add(new TrakHoundTableStep { Type = TrakHoundTableStepType.Insert, Definition = TrakHoundTables.GetDefinition<TItem>(), Rows = TrakHoundTables.GetRows(items), Overwrite = overwrite });
            }
        }

        /// <summary>
        /// Adds a step that updates existing rows (matched by Primary Key) and inserts new rows for the items
        /// </summary>
        public void AppendMerge<TItem>(IEnumerable<TItem> items)
        {
            if (!items.IsNullOrEmpty())
            {
                _steps.Add(new TrakHoundTableStep { Type = TrakHoundTableStepType.Merge, Definition = TrakHoundTables.GetDefinition<TItem>(), Rows = TrakHoundTables.GetRows(items) });
            }
        }

        /// <summary>
        /// Adds a step that deletes rows in batches until none match. The condition is in the "ansi" subset of SQL and uses "target"
        /// as the name of the Table. Each batch commits by itself, so Build the Request with useTransaction set to false
        /// </summary>
        public void AppendDeleteBatched<TItem>(string where, int batchSize = 5000)
        {
            var definition = TrakHoundTables.GetDefinition<TItem>();
            AppendDeleteBatched(definition.Schema, definition.Name, where, batchSize);
        }

        /// <summary>
        /// Adds a step that deletes rows of the named Table in batches until none match (see AppendDeleteBatched of TItem)
        /// </summary>
        public void AppendDeleteBatched(string schema, string table, string where, int batchSize = 5000)
        {
            _steps.Add(new TrakHoundTableStep { Type = TrakHoundTableStepType.DeleteBatched, Schema = schema, Table = table, Where = where, BatchSize = batchSize });
        }

        /// <summary>
        /// Builds the Request (null if nothing was added). When useTransaction is true, either all of the steps apply or none do.
        /// When readOnly is true, the Request only needs the "read" Permission, which only applies when every step is a Query
        /// </summary>
        public TrakHoundTableRequest Build(bool useTransaction = true, bool readOnly = false)
        {
            if (_steps.Count == 0) return null;

            var request = new TrakHoundTableRequest();
            request.Steps = _steps.ToArray();
            request.Parameters = Parameters.Count > 0 ? TrakHoundTableRequest.GetParameters(Parameters) : null;
            request.Timeout = Timeout;
            request.Transaction = useTransaction;
            request.ReadOnly = readOnly;
            return request;
        }
    }
}
