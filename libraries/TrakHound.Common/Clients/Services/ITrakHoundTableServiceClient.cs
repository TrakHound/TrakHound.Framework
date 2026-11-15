// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to list Tables and execute Queries through the Table Service
    /// </summary>
    public interface ITrakHoundTableServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Lists the Tables available through the Router
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the Columns of the specified Table
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the SQL Dialects the Table Service runs, in order of preference (ex. sqlserver, ansi)
        /// </summary>
        Task<TrakHoundServiceResponse<string[]>> GetDialects(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs the steps of a Request and returns a Result for each step. See TrakHoundTableRequestBuilder
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query that does not return results (ex. INSERT, UPDATE, DELETE) using the optional timeout and parameters
        /// </summary>
        Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query and returns each of its Result sets
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query and returns the first value (from the Result set at resultIndex, or the first Result set with a value)
        /// </summary>
        Task<TrakHoundServiceResponse<string>> ReadSingleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query and returns the values from the Result set at resultIndex (or from all Result sets if not specified)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query and returns the first Row mapped to T (Columns are matched to Properties by snake_case name or Column attribute)
        /// </summary>
        Task<TrakHoundServiceResponse<T>> ReadSingle<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a Query and returns each Row mapped to T (Columns are matched to Properties by snake_case name or Column attribute)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default);
    }
}
