// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Globalization;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Sqlite.Service
{
    /// <summary>
    /// A Table Service that runs Queries against a SQLite database file. SQLite has no Schemas, so each Schema is its own file next to
    /// the database (ex. data.trakhound.db for the "trakhound" Schema of data.db), attached under the Schema's name, which lets the
    /// same SQL run as on other databases. Every open connection attaches them, so connections aren't pooled
    /// </summary>
    public class SqliteTableService : TrakHoundSqlTableService
    {
        private readonly string _databasePath;
        private readonly string _connectionString;


        protected override TrakHoundSqlDialect Dialect { get; } = new SqliteDialect();


        public SqliteTableService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            var database = configuration.GetParameter("database");
            if (string.IsNullOrEmpty(database)) database = "trakhound.db";

            _databasePath = Path.GetFullPath(database);

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var builder = new SqliteConnectionStringBuilder();
            builder.DataSource = _databasePath;
            builder.Mode = SqliteOpenMode.ReadWriteCreate;
            builder.Pooling = false;
            _connectionString = builder.ConnectionString;
        }


        protected override DbConnection CreateConnection() => new SqliteConnection(_connectionString);

        protected override DbParameter CreateDbParameter(TrakHoundTableParameter parameter) => CreateParameter(parameter);

        protected override int GetStatusCode(DbException exception)
        {
            if (exception is SqliteException ex)
            {
                // A null in a Column that doesn't allow it is an error in the Query, not a conflict with another row
                return ex.SqliteExtendedErrorCode == 1299 ? 400 : SqliteErrors.GetStatusCode(ex.SqliteErrorCode);
            }

            return 500;
        }

        protected override string GetErrorMessage(DbException exception)
        {
            return exception is SqliteException ex ? $"SqliteException : {ex.SqliteErrorCode} : {ex.Message}" : base.GetErrorMessage(exception);
        }

        protected override async Task OnConnectionOpened(DbConnection connection, CancellationToken cancellationToken)
        {
            // WAL lets readers continue during a write, and it is saved in the file
            await RunCommand(connection, "pragma journal_mode = wal;", cancellationToken);

            var directory = Path.GetDirectoryName(_databasePath);
            var prefix = Path.GetFileNameWithoutExtension(_databasePath) + ".";
            var extension = Path.GetExtension(_databasePath);

            foreach (var file in Directory.GetFiles(directory, $"{prefix}*{extension}"))
            {
                var schema = Path.GetFileNameWithoutExtension(file).Substring(prefix.Length);
                await Attach(connection, schema, cancellationToken);
            }
        }

        // Attaching can't be done in a Transaction, so new Schemas are attached before the Request's Transaction starts
        protected override async Task OnRequestStarting(DbConnection connection, TrakHoundTableRequest request, CancellationToken cancellationToken)
        {
            var schemas = request.Steps.Where(o => o?.Type == TrakHoundTableStepType.EnsureTable).Select(o => o.Definition?.Schema).Where(o => !string.IsNullOrEmpty(o)).Distinct();

            foreach (var schema in schemas)
            {
                if (!File.Exists(GetSchemaPath(schema))) await Attach(connection, schema, cancellationToken);
            }
        }

        public override async Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default)
        {
            return await Run("List", cancellationToken, async (connection, queryId) =>
            {
                var tables = new List<TrakHoundTable>();

                foreach (var schema in await GetSchemas(connection, cancellationToken))
                {
                    await using var command = CreateCommand($"select '{schema.Replace("'", "''")}' as \"schema\", \"name\", case \"type\" when 'view' then 'View' else 'Table' end as \"type\" from {Dialect.QuoteIdentifier(schema)}.sqlite_master where \"type\" in ('table', 'view') and \"name\" not like 'sqlite_%' order by \"name\";", connection, null, null, null);
                    var (results, _) = await ReadResults(command, cancellationToken);

                    var items = results.FirstOrDefault()?.ReadMultiple<TrakHoundTable>();
                    if (items != null) tables.AddRange(items);
                }

                return tables.Count > 0 ? TrakHoundServiceResponse<TrakHoundTable[]>.Ok(tables.ToArray()) : TrakHoundServiceResponse<TrakHoundTable[]>.NotFound();
            });
        }

        public override async Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tableName)) return TrakHoundServiceResponse<TrakHoundTableColumn[]>.BadRequest("No Table Name Specified");

            return await Run("GetColumns", cancellationToken, async (connection, queryId) =>
            {
                var separator = tableName.IndexOf('.');
                var schema = separator > 0 ? tableName.Substring(0, separator) : "main";
                var table = separator > 0 ? tableName.Substring(separator + 1) : tableName;

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("tableName", TrakHoundTableDataType.String, table);
                parameters.Add("schemaName", TrakHoundTableDataType.String, schema);

                var query = "select \"name\", \"type\", \"notnull\" from pragma_table_info(@tableName, @schemaName) order by \"cid\";";

                await using var command = CreateCommand(query, connection, null, null, parameters);
                var (results, _) = await ReadResults(command, cancellationToken);

                var columns = new List<TrakHoundTableColumn>();

                var rows = results.FirstOrDefault()?.Rows;
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var column = new TrakHoundTableColumn();
                        column.Name = row[0] as string;
                        column.DataType = GetDataType(row[1] as string);
                        column.AllowNull = Convert.ToInt64(row[2]) == 0;
                        columns.Add(column);
                    }
                }

                // No columns means the Table (or View) doesn't exist
                return columns.Count > 0 ? TrakHoundServiceResponse<TrakHoundTableColumn[]>.Ok(columns.ToArray()) : TrakHoundServiceResponse<TrakHoundTableColumn[]>.NotFound($"Table Not Found : {tableName}");
            });
        }


        /// <summary>
        /// Gets the Data Type for a declared SQLite Column type
        /// </summary>
        public static TrakHoundTableDataType GetDataType(string typeName)
        {
            switch (typeName?.ToLowerInvariant())
            {
                case "integer": return TrakHoundTableDataType.Int64;
                case "real": return TrakHoundTableDataType.Float;
                case "numeric": return TrakHoundTableDataType.Decimal;
                case "blob": return TrakHoundTableDataType.Binary;
                default: return TrakHoundTableDataType.String;
            }
        }

        /// <summary>
        /// Creates the SqliteParameter for a Table Parameter. Guids are stored in lowercase so they compare as other databases do, and booleans as 1 or 0
        /// </summary>
        public static SqliteParameter CreateParameter(TrakHoundTableParameter parameter)
        {
            var name = parameter.Name.StartsWith('@') ? parameter.Name : $"@{parameter.Name}";

            object value = parameter.Value;
            switch (value)
            {
                case null:
                case DBNull:
                    value = DBNull.Value;
                    break;
                case Guid guid:
                    value = guid.ToString();
                    break;
                case string s when parameter.DataType == TrakHoundTableDataType.Guid:
                    value = s.ToLowerInvariant();
                    break;
                case bool b:
                    value = b ? 1 : 0;
                    break;
                case decimal d:
                    value = d.ToString(CultureInfo.InvariantCulture);
                    break;
            }

            return new SqliteParameter(name, value);
        }


        private string GetSchemaPath(string schema)
        {
            var name = $"{Path.GetFileNameWithoutExtension(_databasePath)}.{schema}{Path.GetExtension(_databasePath)}";
            return Path.Combine(Path.GetDirectoryName(_databasePath), name);
        }

        private async Task Attach(DbConnection connection, string schema, CancellationToken cancellationToken)
        {
            var parameter = new SqliteParameter("@path", GetSchemaPath(schema));

            await using var command = connection.CreateCommand();
            command.CommandText = $"attach database @path as {Dialect.QuoteIdentifier(schema)};";
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await RunCommand(connection, $"pragma {Dialect.QuoteIdentifier(schema)}.journal_mode = wal;", cancellationToken);
        }

        private static async Task RunCommand(DbConnection connection, string text, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = text;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<List<string>> GetSchemas(DbConnection connection, CancellationToken cancellationToken)
        {
            var schemas = new List<string>();

            await using var command = connection.CreateCommand();
            command.CommandText = "select \"name\" from pragma_database_list where \"name\" <> 'temp' order by \"seq\";";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) schemas.Add(reader.GetString(0));

            return schemas;
        }
    }
}
