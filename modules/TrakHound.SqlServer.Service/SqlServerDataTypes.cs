// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Services.Tables;

namespace TrakHound.SqlServer.Service
{
    /// <summary>
    /// Maps SQL Server types to Table Data Types
    /// </summary>
    public static class SqlServerDataTypes
    {
        /// <summary>
        /// Gets the Data Type for the CLR type a column is read as (ex. SqlDataReader.GetFieldType)
        /// </summary>
        public static TrakHoundTableDataType GetDataType(Type type) => TrakHoundSqlTableService.GetDataType(type);

        /// <summary>
        /// Gets the Data Type for a SQL Server type name (ex. nvarchar, bigint)
        /// </summary>
        public static TrakHoundTableDataType GetDataType(string typeName)
        {
            switch (typeName?.ToLowerInvariant())
            {
                case "tinyint": return TrakHoundTableDataType.Byte;
                case "smallint": return TrakHoundTableDataType.Int16;
                case "int": return TrakHoundTableDataType.Int32;
                case "bigint": return TrakHoundTableDataType.Int64;
                case "real":
                case "float": return TrakHoundTableDataType.Float;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney": return TrakHoundTableDataType.Decimal;
                case "bit": return TrakHoundTableDataType.Boolean;
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "datetimeoffset": return TrakHoundTableDataType.Timestamp;
                case "time": return TrakHoundTableDataType.Duration;
                case "uniqueidentifier": return TrakHoundTableDataType.Guid;
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion": return TrakHoundTableDataType.Binary;
                default: return TrakHoundTableDataType.String;
            }
        }

        /// <summary>
        /// Returns true if the SQL Server type always stores its full length (ex. char, nchar, binary)
        /// </summary>
        public static bool IsFixedLength(string typeName)
        {
            switch (typeName?.ToLowerInvariant())
            {
                case "char":
                case "nchar":
                case "binary":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the length of a string or binary column in characters or bytes from sys.columns max_length
        /// (null for (max) and for other types)
        /// </summary>
        public static int? GetDataLength(string typeName, int maxLength)
        {
            if (maxLength < 0) return null; // (max)

            switch (typeName?.ToLowerInvariant())
            {
                case "char":
                case "varchar":
                case "binary":
                case "varbinary":
                    return maxLength;
                case "nchar":
                case "nvarchar":
                    return maxLength / 2; // max_length is in bytes
                default:
                    return null;
            }
        }
    }
}
