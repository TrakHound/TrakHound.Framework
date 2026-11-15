// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Table Service for a SQL database that has an ADO.NET driver. A Service supplies its connection, Dialect, Parameters and
    /// error Status Codes, and lists its Tables and Columns from its own catalog
    /// </summary>
    public abstract class TrakHoundSqlTableService : TrakHoundService, ITrakHoundTableService
    {
        private const int _defaultCommandTimeout = 60;

        private readonly CacheDictionary<string, TrakHoundServiceAvailabilityResponse> _availabilityCache = new CacheDictionary<string, TrakHoundServiceAvailabilityResponse>(60000, 5000);


        /// <summary>
        /// The Dialect of the database
        /// </summary>
        protected abstract TrakHoundSqlDialect Dialect { get; }


        /// <summary>
        /// Creates a Service with the specified Configuration
        /// </summary>
        protected TrakHoundSqlTableService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        /// <summary>
        /// Creates a connection that hasn't been opened
        /// </summary>
        protected abstract DbConnection CreateConnection();

        /// <summary>
        /// Creates the database Parameter for a Table Parameter (a new one for each command, since a Parameter can only belong to one)
        /// </summary>
        protected abstract DbParameter CreateDbParameter(TrakHoundTableParameter parameter);

        /// <summary>
        /// Gets the Status Code for a database error. Errors in the Query must not be Internal Errors, so a Router doesn't run them again on the next Target
        /// </summary>
        protected abstract int GetStatusCode(DbException exception);

        /// <inheritdoc/>
        public abstract Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default);


        /// <summary>
        /// Gets the message for a database error
        /// </summary>
        protected virtual string GetErrorMessage(DbException exception) => $"{exception.GetType().Name} : {exception.ErrorCode} : {exception.Message}";

        /// <summary>
        /// Gets why the Service can't be used before it tries the database, such as an invalid License (null if it can)
        /// </summary>
        protected virtual string GetUnavailableMessage() => null;

        /// <summary>
        /// Runs after a connection is opened, before anything else runs on it
        /// </summary>
        protected virtual Task OnConnectionOpened(DbConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Runs before the Request's steps and outside of its Transaction
        /// </summary>
        protected virtual Task OnRequestStarting(DbConnection connection, TrakHoundTableRequest request, CancellationToken cancellationToken) => Task.CompletedTask;


        /// <inheritdoc/>
        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var cacheKey = Id ?? "";
            if (_availabilityCache.TryGet(cacheKey, out var cachedResponse))
            {
                if (IsLogEnabled(TrakHoundLogLevel.Debug)) Log(TrakHoundLogLevel.Debug, $"{Dialect.Name} : CheckAvailability : Read From Cache : Availability = {cachedResponse.IsAvailable}");
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(cachedResponse);
            }

            var response = new TrakHoundServiceAvailabilityResponse();

            var unavailableMessage = GetUnavailableMessage();
            if (unavailableMessage == null)
            {
                try
                {
                    if (IsLogEnabled(TrakHoundLogLevel.Debug)) Log(TrakHoundLogLevel.Debug, $"{Dialect.Name} : CheckAvailability : Connecting to Database..");

                    await using (var connection = CreateConnection())
                    {
                        await connection.OpenAsync(cancellationToken);
                        await OnConnectionOpened(connection, cancellationToken);

                        await using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "select 1";
                            await command.ExecuteScalarAsync(cancellationToken);
                        }

                        response.IsAvailable = true;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (DbException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ex.Message, ex, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Return the reason (ex. Login failed) so an unavailable Service can be diagnosed
                    Log(TrakHoundLogLevel.Error, $"{Dialect.Name} : CheckAvailability : {ex.HResult} : {ex.Message}");
                    response.Message = ex.Message;
                }
            }
            else
            {
                response.Message = unavailableMessage;
            }

            _availabilityCache.Add(cacheKey, response);

            return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(response);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<string[]>> GetDialects(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrakHoundServiceResponse<string[]>.Ok(Dialect.SupportedDialects));
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
        {
            var response = await RunStatements(query, cancellationToken, async (connection, statements) =>
            {
                var messages = new List<string>();

                foreach (var statement in statements)
                {
                    await using var command = CreateCommand(statement, connection, null, timeout, parameters);

                    var recordsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                    messages.Add(GetMessage(recordsAffected));
                }

                return TrakHoundServiceResponse<string>.Ok(string.Join("\r\n", messages));
            });

            return new TrakHoundServiceResponse(response.StatusCode, response.Success ? response.Result : response.Message);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
        {
            return await RunStatements(query, cancellationToken, async (connection, statements) =>
            {
                var tableResults = new List<TrakHoundTableResult>();

                foreach (var statement in statements)
                {
                    await using var command = CreateCommand(statement, connection, null, timeout, parameters);
                    tableResults.AddRange(await ReadCommandResults(command, cancellationToken));
                }

                return TrakHoundServiceResponse<TrakHoundTableResult[]>.Ok(tableResults.ToArray());
            });
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null || request.Steps.IsNullOrEmpty()) return TrakHoundServiceResponse<TrakHoundTableStepResult[]>.BadRequest("No Steps Specified");

            if (request.Transaction && request.Steps.Any(o => o?.Type == TrakHoundTableStepType.DeleteBatched))
            {
                return TrakHoundServiceResponse<TrakHoundTableStepResult[]>.BadRequest("A Batched Delete can't run in a Transaction");
            }

            // Compile every step first, so a step that can't run here means that nothing runs
            var compiled = new List<CompiledStep>();
            foreach (var step in request.Steps)
            {
                var compileResult = Compile(step);
                if (compileResult.Error != null) return compileResult.Error.Value;
                compiled.Add(compileResult.Step);
            }

            return await Run("Execute", cancellationToken, async (connection, queryId) =>
            {
                await OnRequestStarting(connection, request, cancellationToken);

                var parameters = request.GetParameters();
                var results = new List<TrakHoundTableStepResult>();

                DbTransaction transaction = null;
                if (request.Transaction) transaction = await connection.BeginTransactionAsync(cancellationToken);

                try
                {
                    foreach (var step in compiled)
                    {
                        results.Add(await RunStep(step, connection, transaction, request.Timeout, parameters, cancellationToken));
                    }

                    if (transaction != null) await transaction.CommitAsync(cancellationToken);
                }
                finally
                {
                    // Rolls back when it wasn't committed
                    if (transaction != null) await transaction.DisposeAsync();
                }

                return TrakHoundServiceResponse<TrakHoundTableStepResult[]>.Ok(results.ToArray());
            });
        }


        /// <summary>
        /// Opens a connection, runs the function and converts errors to Responses
        /// </summary>
        protected async Task<TrakHoundServiceResponse<TResult>> Run<TResult>(string query, CancellationToken cancellationToken, Func<DbConnection, string, Task<TrakHoundServiceResponse<TResult>>> run)
        {
            var queryId = Guid.NewGuid().ToString();

            try
            {
                if (IsLogEnabled(TrakHoundLogLevel.Debug)) Log(TrakHoundLogLevel.Debug, $"{Dialect.Name} : {queryId} : Connecting to Database..");
                if (IsLogEnabled(TrakHoundLogLevel.Trace)) Log(TrakHoundLogLevel.Trace, $"SQL Query : {queryId} : {query}");

                await using (var connection = CreateConnection())
                {
                    await connection.OpenAsync(cancellationToken);
                    await OnConnectionOpened(connection, cancellationToken);

                    var response = await run(connection, queryId);

                    if (IsLogEnabled(TrakHoundLogLevel.Debug)) Log(TrakHoundLogLevel.Debug, $"{Dialect.Name} : {queryId} : Query Executed Successfully");
                    return response;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DbException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            catch (DbException ex)
            {
                var message = GetErrorMessage(ex);
                Log(TrakHoundLogLevel.Error, $"{queryId} : {message}");
                if (IsLogEnabled(TrakHoundLogLevel.Trace)) Log(TrakHoundLogLevel.Trace, $"{queryId} : {message} : {query}");

                return new TrakHoundServiceResponse<TResult>(default, GetStatusCode(ex), message);
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Exception : {queryId} : {ex.HResult} : {ex.Message}");
                if (IsLogEnabled(TrakHoundLogLevel.Trace)) Log(TrakHoundLogLevel.Trace, $"Exception : {queryId} : {ex.HResult} : {ex.Message} : {query}");
                return TrakHoundServiceResponse<TResult>.InternalError($"Exception : {ex.HResult} : {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a command for the Statement with the specified timeout (null or zero uses the default) and Parameters
        /// </summary>
        protected DbCommand CreateCommand(string statement, DbConnection connection, DbTransaction transaction, int? timeout, TrakHoundTableParameterCollection parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = statement;
            command.Transaction = transaction;
            command.CommandTimeout = timeout.HasValue && timeout.Value > 0 ? timeout.Value : _defaultCommandTimeout;

            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter != null && !string.IsNullOrEmpty(parameter.Name))
                    {
                        command.Parameters.Add(CreateDbParameter(parameter));
                    }
                }
            }

            return command;
        }

        /// <summary>
        /// Reads every result set of the command. RecordsAffected is only set once the reader is closed
        /// </summary>
        protected static async Task<(List<TrakHoundTableResult> Results, int RecordsAffected)> ReadResults(DbCommand command, CancellationToken cancellationToken)
        {
            var results = new List<TrakHoundTableResult>();

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            await using (reader)
            {
                do
                {
                    var columns = new TrakHoundTableColumn[reader.FieldCount];
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var column = new TrakHoundTableColumn();
                        column.Name = reader.GetName(i);
                        column.DataType = GetDataType(reader.GetFieldType(i));
                        columns[i] = column;
                    }

                    var rows = new List<object[]>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var row = new object[columns.Length];
                        reader.GetValues(row);
                        for (var i = 0; i < row.Length; i++)
                        {
                            if (row[i] == DBNull.Value) row[i] = null;
                        }
                        rows.Add(row);
                    }

                    results.Add(new TrakHoundTableResult { Columns = columns, Rows = rows.ToArray() });
                }
                while (await reader.NextResultAsync(cancellationToken));
            }

            return (results, reader.RecordsAffected);
        }

        /// <summary>
        /// Gets the Data Type for the CLR type a Column is read as
        /// </summary>
        public static TrakHoundTableDataType GetDataType(Type type)
        {
            if (type == typeof(byte)) return TrakHoundTableDataType.Byte;
            if (type == typeof(short)) return TrakHoundTableDataType.Int16;
            if (type == typeof(int)) return TrakHoundTableDataType.Int32;
            if (type == typeof(long)) return TrakHoundTableDataType.Int64;
            if (type == typeof(float)) return TrakHoundTableDataType.Float;
            if (type == typeof(double)) return TrakHoundTableDataType.Float;
            if (type == typeof(decimal)) return TrakHoundTableDataType.Decimal;
            if (type == typeof(bool)) return TrakHoundTableDataType.Boolean;
            if (type == typeof(DateTime)) return TrakHoundTableDataType.Timestamp;
            if (type == typeof(DateTimeOffset)) return TrakHoundTableDataType.Timestamp;
            if (type == typeof(TimeSpan)) return TrakHoundTableDataType.Duration;
            if (type == typeof(Guid)) return TrakHoundTableDataType.Guid;
            if (type == typeof(byte[])) return TrakHoundTableDataType.Binary;

            return TrakHoundTableDataType.String;
        }


        private sealed class CompiledStep
        {
            public TrakHoundTableStep Step { get; init; }
            public IEnumerable<TrakHoundSqlStatement> Statements { get; init; }
        }

        private (CompiledStep Step, TrakHoundServiceResponse<TrakHoundTableStepResult[]>? Error) Compile(TrakHoundTableStep step)
        {
            TrakHoundServiceResponse<TrakHoundTableStepResult[]> BadRequest(string message) => TrakHoundServiceResponse<TrakHoundTableStepResult[]>.BadRequest(message);

            if (step == null) return (null, BadRequest("Invalid Step"));

            try
            {
                switch (step.Type)
                {
                    case TrakHoundTableStepType.Query:
                        var query = step.Variants?.Select(Dialect.SupportedDialects);
                        if (string.IsNullOrEmpty(query))
                        {
                            // 405 = this Target doesn't handle the Request, so a Router tries the next Target (nothing has run)
                            var message = $"No Query Variant for ({string.Join(", ", Dialect.SupportedDialects)})";
                            return (null, new TrakHoundServiceResponse<TrakHoundTableStepResult[]>(default, 405, message));
                        }

                        var batches = Dialect.SplitScript(Dialect.AddPaging(query, step.Skip, step.Take));
                        if (batches.IsNullOrEmpty()) return (null, BadRequest("Invalid Query"));
                        return (new CompiledStep { Step = step, Statements = batches.Select(o => new TrakHoundSqlStatement(o)).ToList() }, null);

                    case TrakHoundTableStepType.EnsureTable:
                        if (!IsValid(step.Definition)) return (null, BadRequest("Invalid Table Definition"));
                        return (new CompiledStep { Step = step, Statements = Dialect.GenerateEnsureTable(step.Definition).ToList() }, null);

                    case TrakHoundTableStepType.Insert:
                        if (!IsValid(step.Definition)) return (null, BadRequest("Invalid Table Definition"));
                        return (new CompiledStep { Step = step, Statements = Dialect.GenerateInsert(step.Definition, ConvertRows(step), step.Overwrite).ToList() }, null);

                    case TrakHoundTableStepType.Merge:
                        if (!IsValid(step.Definition)) return (null, BadRequest("Invalid Table Definition"));
                        return (new CompiledStep { Step = step, Statements = Dialect.GenerateMerge(step.Definition, ConvertRows(step)).ToList() }, null);

                    case TrakHoundTableStepType.DeleteBatched:
                        if (string.IsNullOrEmpty(step.Table) || string.IsNullOrEmpty(step.Where) || step.BatchSize < 1) return (null, BadRequest("A Batched Delete needs a Table, a condition and a Batch Size"));
                        return (new CompiledStep { Step = step, Statements = new[] { new TrakHoundSqlStatement(Dialect.GenerateDeleteBatch(step.Table, step.Schema, step.Where, step.BatchSize)) } }, null);

                    default:
                        return (null, BadRequest($"Invalid Step Type : {step.Type}"));
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return (null, BadRequest(ex.Message));
            }
        }

        private static bool IsValid(TrakHoundTableDefinition definition)
        {
            return definition != null && !string.IsNullOrEmpty(definition.Name) && !definition.Columns.IsNullOrEmpty();
        }

        // Rows from JSON hold generic values, so they are converted to each Column's type
        private static object[][] ConvertRows(TrakHoundTableStep step)
        {
            if (step.Rows.IsNullOrEmpty()) return Array.Empty<object[]>();

            var columns = step.Definition.Columns;
            return step.Rows.Select(row => row?.Select((value, i) => i < columns.Length ? TrakHoundTableRequest.ConvertValue(columns[i].DataType, value) : value).ToArray()).ToArray();
        }

        private async Task<TrakHoundTableStepResult> RunStep(CompiledStep compiled, DbConnection connection, DbTransaction transaction, int? timeout, TrakHoundTableParameterCollection parameters, CancellationToken cancellationToken)
        {
            var stepResult = new TrakHoundTableStepResult();

            switch (compiled.Step.Type)
            {
                case TrakHoundTableStepType.Query:
                    var tableResults = new List<TrakHoundTableResult>();
                    foreach (var statement in compiled.Statements)
                    {
                        await using var command = CreateCommand(statement.Text, connection, transaction, timeout, parameters);
                        tableResults.AddRange(await ReadCommandResults(command, cancellationToken));
                    }
                    stepResult.Results = tableResults.ToArray();
                    stepResult.Message = tableResults.LastOrDefault()?.Message;
                    break;

                case TrakHoundTableStepType.DeleteBatched:
                    var deleted = 0;
                    int batchDeleted;
                    do
                    {
                        // Each batch commits by itself, so a long delete doesn't hold one large Transaction
                        await using var command = CreateCommand(compiled.Statements.First().Text, connection, transaction, timeout, parameters);
                        batchDeleted = await command.ExecuteNonQueryAsync(cancellationToken);
                        deleted += Math.Max(0, batchDeleted);
                    }
                    while (batchDeleted > 0);
                    stepResult.Message = GetMessage(deleted);
                    break;

                default:
                    var affected = 0;
                    foreach (var statement in compiled.Statements)
                    {
                        await using var command = CreateCommand(statement.Text, connection, transaction, timeout, statement.Parameters);
                        affected += Math.Max(0, await command.ExecuteNonQueryAsync(cancellationToken));
                    }
                    stepResult.Message = GetMessage(affected);
                    break;
            }

            return stepResult;
        }

        // RecordsAffected is the total for the command, so it is reported once (on its last result)
        private static async Task<List<TrakHoundTableResult>> ReadCommandResults(DbCommand command, CancellationToken cancellationToken)
        {
            var (results, recordsAffected) = await ReadResults(command, cancellationToken);
            if (results.Count > 0) results[results.Count - 1].Message = GetMessage(recordsAffected);
            return results;
        }

        // Splits the Query into batches and runs them on one connection
        private Task<TrakHoundServiceResponse<TResult>> RunStatements<TResult>(string query, CancellationToken cancellationToken, Func<DbConnection, IEnumerable<string>, Task<TrakHoundServiceResponse<TResult>>> run)
        {
            if (string.IsNullOrEmpty(query)) return Task.FromResult(TrakHoundServiceResponse<TResult>.BadRequest("No Query Specified"));

            var statements = Dialect.SplitScript(query);
            if (statements.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse<TResult>.BadRequest("Invalid Query"));

            return Run(query, cancellationToken, (connection, queryId) =>
            {
                if (IsLogEnabled(TrakHoundLogLevel.Debug)) Log(TrakHoundLogLevel.Debug, $"{Dialect.Name} : {queryId} : Executing Query..");
                return run(connection, statements);
            });
        }

        private static string GetMessage(int recordsAffected)
        {
            return recordsAffected >= 0 ? $"({recordsAffected} rows affected)" : "Commands completed successfully.";
        }
    }
}
