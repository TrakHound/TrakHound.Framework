// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Text;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.SqlServer.Service
{
    /// <summary>
    /// A Table Service that runs Queries against SQL Server. Scripts are split into batches on GO lines, and each batch
    /// is run as its own command (on one connection). SQL errors are returned with the Status Code from SqlServerErrors
    /// </summary>
    public class SqlServerTableService : SqlServerService
    {
        private const string _listQuery = @"select
[schema].[name] as [schema],
[table].[name] as [name],
[type] = 'Table'
from sys.tables as [table]
inner join sys.schemas as [schema] on [table].[schema_id] = [schema].[schema_id]

union all

select
[schema].[name] as [schema],
[view].[name] as [name],
[type] = 'View'
from sys.views as [view]
inner join sys.schemas as [schema] on [view].[schema_id] = [schema].[schema_id]

order by [schema], [name];";

        // TYPE_NAME of the system type so alias types (ex. sysname) report their base type
        private const string _getColumnsQuery = @"select
[column].[name],
TYPE_NAME([column].[system_type_id]) as [type_name],
[column].[max_length],
[column].[is_nullable]
from sys.columns as [column]
where [column].[object_id] = OBJECT_ID(@tableName)
order by [column].[column_id];";

        private readonly string _connectionString;


        protected override TrakHoundSqlDialect Dialect { get; } = new SqlServerDialect();


        public SqlServerTableService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            _connectionString = SqlServerConnectionString.Create(configuration, $"TrakHound-Instance:{InstanceId}");
        }


        protected override DbConnection CreateConnection() => new SqlConnection(_connectionString);

        protected override DbParameter CreateDbParameter(TrakHoundTableParameter parameter) => CreateParameter(parameter);

        protected override int GetStatusCode(DbException exception)
        {
            return exception is SqlException sqlException ? SqlServerErrors.GetStatusCode(sqlException.Number) : 500;
        }

        protected override string GetErrorMessage(DbException exception)
        {
            return exception is SqlException ex ? $"SqlException : {ex.Number} : Line {ex.LineNumber} : {ex.Message}" : base.GetErrorMessage(exception);
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
                var parameters = new TrakHoundTableParameterCollection();
                parameters.Add("tableName", TrakHoundTableDataType.String, tableName);

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
                        column.DataType = SqlServerDataTypes.GetDataType(typeName);
                        column.DataLength = SqlServerDataTypes.GetDataLength(typeName, Convert.ToInt32(row[2]));
                        column.FixedLength = SqlServerDataTypes.IsFixedLength(typeName);
                        column.AllowNull = row[3] is bool allowNull && allowNull;
                        columns.Add(column);
                    }
                }

                // No columns means the Table (or View) doesn't exist
                return columns.Count > 0 ? TrakHoundServiceResponse<TrakHoundTableColumn[]>.Ok(columns.ToArray()) : TrakHoundServiceResponse<TrakHoundTableColumn[]>.NotFound($"Table Not Found : {tableName}");
            });
        }


        /// <summary>
        /// Creates the SqlParameter for a Table Parameter. Strings that aren't plain ASCII are sent as nvarchar so their
        /// characters are kept (varchar would replace characters outside the server's code page with '?')
        /// </summary>
        public static SqlParameter CreateParameter(TrakHoundTableParameter parameter)
        {
            var name = parameter.Name.StartsWith('@') ? parameter.Name : $"@{parameter.Name}";
            var value = parameter.Value ?? DBNull.Value;

            var dbType = GetSqlDbType(parameter.DataType);
            if (dbType == SqlDbType.VarChar && value is string s && !Ascii.IsValid(s)) dbType = SqlDbType.NVarChar;

            if (!dbType.HasValue)
            {
                // Infer the type from the Value
                return new SqlParameter(name, value);
            }

            var sqlParameter = new SqlParameter(name, dbType.Value);

            // Use (max) so that strings are never truncated
            if (dbType == SqlDbType.VarChar || dbType == SqlDbType.NVarChar) sqlParameter.Size = -1;

            sqlParameter.Value = value;
            return sqlParameter;
        }

        private static SqlDbType? GetSqlDbType(TrakHoundTableDataType? dataType)
        {
            switch (dataType)
            {
                case TrakHoundTableDataType.Int64: return SqlDbType.BigInt;
                case TrakHoundTableDataType.Int32: return SqlDbType.Int;
                case TrakHoundTableDataType.Int16: return SqlDbType.SmallInt;
                case TrakHoundTableDataType.Byte: return SqlDbType.TinyInt;
                case TrakHoundTableDataType.Boolean: return SqlDbType.Bit;
                case TrakHoundTableDataType.Float: return SqlDbType.Float;
                case TrakHoundTableDataType.String: return SqlDbType.VarChar;
                case TrakHoundTableDataType.Guid: return SqlDbType.UniqueIdentifier;
                default: return null;
            }
        }
    }
}
