// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Npgsql;
using NpgsqlTypes;
using System.Data.Common;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Postgres.Service
{
    /// <summary>
    /// A Table Service that runs Queries against PostgreSQL. Errors are returned with the Status Code from PostgresErrors
    /// </summary>
    public class PostgresTableService : TrakHoundSqlTableService
    {
        private const string _listQuery = @"select table_schema as ""schema"", table_name as ""name"", case table_type when 'VIEW' then 'View' else 'Table' end as ""type""
from information_schema.tables
where table_schema not in ('pg_catalog', 'information_schema')
order by 1, 2;";

        private const string _getColumnsQuery = @"select column_name, data_type, character_maximum_length, is_nullable
from information_schema.columns
where table_schema = coalesce(@schemaName, current_schema()) and table_name = @tableName
order by ordinal_position;";

        private readonly string _connectionString;


        protected override TrakHoundSqlDialect Dialect { get; } = new PostgresDialect();


        public PostgresTableService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            _connectionString = PostgresConnectionString.Create(configuration, $"TrakHound-Instance:{InstanceId}");
        }


        protected override DbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        protected override DbParameter CreateDbParameter(TrakHoundTableParameter parameter) => CreateParameter(parameter);

        protected override int GetStatusCode(DbException exception)
        {
            return PostgresErrors.GetStatusCode((exception as PostgresException)?.SqlState);
        }

        protected override string GetErrorMessage(DbException exception)
        {
            return exception is PostgresException ex ? $"PostgresException : {ex.SqlState} : {ex.MessageText}" : base.GetErrorMessage(exception);
        }

        public override async Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default)
        {
            return await Run(_listQuery, cancellationToken, async (connection, queryId) =>
            {
                await using var command = CreateCommand(_listQuery, connection, null, null, null);
                var (results, _) = await ReadResults(command, cancellationToken);

                var tables = results.FirstOrDefault()?.ReadMultiple<TrakHoundTable>();
                return !tables.IsNullOrEmpty() ? TrakHoundServiceResponse<TrakHoundTable[]>.Ok(tables.ToArray()) : TrakHoundServiceResponse<TrakHoundTable[]>.NotFound();
            });
        }

        public override async Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tableName)) return TrakHoundServiceResponse<TrakHoundTableColumn[]>.BadRequest("No Table Name Specified");

            return await Run(_getColumnsQuery, cancellationToken, async (connection, queryId) =>
            {
                var separator = tableName.IndexOf('.');

                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("schemaName", TrakHoundTableDataType.String, separator > 0 ? tableName.Substring(0, separator) : null);
                parameters.Add("tableName", TrakHoundTableDataType.String, separator > 0 ? tableName.Substring(separator + 1) : tableName);

                await using var command = CreateCommand(_getColumnsQuery, connection, null, null, parameters);
                var (results, _) = await ReadResults(command, cancellationToken);

                var columns = new List<TrakHoundTableColumn>();

                var rows = results.FirstOrDefault()?.Rows;
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var typeName = row[1] as string;

                        var column = new TrakHoundTableColumn();
                        column.Name = row[0] as string;
                        column.DataType = GetDataType(typeName);
                        column.DataLength = row[2] != null ? Convert.ToInt32(row[2]) : null;
                        column.FixedLength = typeName == "character";
                        column.AllowNull = row[3] as string == "YES";
                        columns.Add(column);
                    }
                }

                // No columns means the Table (or View) doesn't exist
                return columns.Count > 0 ? TrakHoundServiceResponse<TrakHoundTableColumn[]>.Ok(columns.ToArray()) : TrakHoundServiceResponse<TrakHoundTableColumn[]>.NotFound($"Table Not Found : {tableName}");
            });
        }


        /// <summary>
        /// Gets the Data Type for a PostgreSQL type name (as information_schema reports it)
        /// </summary>
        public static TrakHoundTableDataType GetDataType(string typeName)
        {
            switch (typeName)
            {
                case "smallint": return TrakHoundTableDataType.Int16;
                case "integer": return TrakHoundTableDataType.Int32;
                case "bigint": return TrakHoundTableDataType.Int64;
                case "real":
                case "double precision": return TrakHoundTableDataType.Float;
                case "numeric":
                case "money": return TrakHoundTableDataType.Decimal;
                case "boolean": return TrakHoundTableDataType.Boolean;
                case "date":
                case "timestamp without time zone":
                case "timestamp with time zone": return TrakHoundTableDataType.Timestamp;
                case "interval":
                case "time without time zone": return TrakHoundTableDataType.Duration;
                case "uuid": return TrakHoundTableDataType.Guid;
                case "bytea": return TrakHoundTableDataType.Binary;
                default: return TrakHoundTableDataType.String;
            }
        }

        /// <summary>
        /// Creates the NpgsqlParameter for a Table Parameter. PostgreSQL doesn't convert implicitly as much as other
        /// databases, so values are converted to the Parameter's Data Type (ex. 1 or 0 to a boolean, a string to a uuid)
        /// </summary>
        public static NpgsqlParameter CreateParameter(TrakHoundTableParameter parameter)
        {
            var name = parameter.Name.StartsWith('@') ? parameter.Name.Substring(1) : parameter.Name;
            var value = parameter.Value == null || parameter.Value is DBNull ? null : parameter.Value;

            var npgsqlParameter = new NpgsqlParameter();
            npgsqlParameter.ParameterName = name;

            switch (parameter.DataType)
            {
                case TrakHoundTableDataType.Boolean:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Boolean;
                    if (value != null) value = value is string s ? bool.Parse(s) : Convert.ToBoolean(value);
                    break;
                case TrakHoundTableDataType.Guid:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Uuid;
                    if (value is string g) value = Guid.Parse(g);
                    break;
                case TrakHoundTableDataType.Byte:
                case TrakHoundTableDataType.Int16:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Smallint;
                    if (value != null) value = Convert.ToInt16(value);
                    break;
                case TrakHoundTableDataType.Int32:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Integer;
                    if (value != null) value = Convert.ToInt32(value);
                    break;
                case TrakHoundTableDataType.Int64:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Bigint;
                    if (value != null) value = Convert.ToInt64(value);
                    break;
                case TrakHoundTableDataType.Float:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Double;
                    if (value != null) value = Convert.ToDouble(value);
                    break;
                case TrakHoundTableDataType.Decimal:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Numeric;
                    if (value != null) value = Convert.ToDecimal(value);
                    break;
                case TrakHoundTableDataType.String:
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Text;
                    if (value != null && !(value is string)) value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }

            npgsqlParameter.Value = value ?? DBNull.Value;
            return npgsqlParameter;
        }
    }
}
