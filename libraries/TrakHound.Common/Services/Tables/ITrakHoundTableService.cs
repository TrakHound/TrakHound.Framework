// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Service that runs Queries against a relational database (ex. SQL Server)
    /// </summary>
    public interface ITrakHoundTableService : ITrakHoundService
    {
        /// <summary>
        /// Lists the Tables and Views in the database
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the Columns of the specified Table
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the SQL Dialects the Service runs, in order of preference (ex. sqlserver, ansi)
        /// </summary>
        Task<TrakHoundServiceResponse<string[]>> GetDialects(CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a Query, in the native SQL of the database, that does not return rows. The timeout is in seconds (null uses the Service's default)
        /// </summary>
        Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a Query, in the native SQL of the database, and returns a Result for each result set. The timeout is in seconds (null uses the Service's default)
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs the steps of a Request and returns a Result for each step. Nothing runs if a step can't run on this Service:
        /// a 405 is returned when a Query step has no Variant for its Dialects, so a Router tries the next Target
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, CancellationToken cancellationToken = default);
    }
}
