// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A result set returned from running a Query
    /// </summary>
    public class TrakHoundTableResult
    {
        /// <summary>
        /// A message returned by the database (ex. the number of rows affected)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// The Columns of the result set
        /// </summary>
        [JsonPropertyName("columns")]
        public TrakHoundTableColumn[] Columns { get; set; }

        /// <summary>
        /// The rows of the result set. Each row contains a value for each Column (in Column order)
        /// </summary>
        [JsonPropertyName("rows")]
        [JsonConverter(typeof(ObjectJaggedArrayJsonConverter))]
        public object[][] Rows { get; set; }


        /// <summary>
        /// Reads the value of the first Column of the first row as a string (null if there are no rows or the value is null)
        /// </summary>
        public string ReadSingleString() => ReadSingleString(this);

        /// <summary>
        /// Reads the non-null values of the first Column as strings (null if there are no rows)
        /// </summary>
        public IEnumerable<string> ReadMultipleString() => ReadMultipleString(this);

        /// <summary>
        /// Reads the first row into a new T (default if there are no rows). See ReadMultiple for how rows are read
        /// </summary>
        public T ReadSingle<T>() => ReadSingle<T>(this);

        /// <summary>
        /// Reads each row into a new T (null if there are no rows). Columns are matched to properties by Column name (ignoring case),
        /// and scalar types (ex. string, long, Guid) read the first Column. Throws a FormatException if a value can't be converted
        /// </summary>
        public IEnumerable<T> ReadMultiple<T>() => ReadMultiple<T>(this);


        /// <summary>
        /// Reads the value of the first Column of the first row as a string (null if there are no rows or the value is null)
        /// </summary>
        public static string ReadSingleString(TrakHoundTableResult tableResult)
        {
            if (tableResult == null || tableResult.Rows.IsNullOrEmpty()) return default;

            return TrakHoundTableReader.ReadString(tableResult.Rows[0]);
        }

        /// <summary>
        /// Reads the non-null values of the first Column as strings (null if there are no rows)
        /// </summary>
        public static IEnumerable<string> ReadMultipleString(TrakHoundTableResult tableResult)
        {
            if (tableResult == null || tableResult.Rows.IsNullOrEmpty()) return default;

            return tableResult.Rows.Select(TrakHoundTableReader.ReadString).Where(o => o != null).ToList();
        }

        /// <summary>
        /// Reads the first row into a new T (default if there are no rows). See ReadMultiple for how rows are read
        /// </summary>
        public static T ReadSingle<T>(TrakHoundTableResult tableResult)
        {
            return TrakHoundTableReader.Read<T>(tableResult).FirstOrDefault();
        }

        /// <summary>
        /// Reads each row into a new T (null if there are no rows). Columns are matched to properties by Column name (ignoring case),
        /// and scalar types (ex. string, long, Guid) read the first Column. Throws a FormatException if a value can't be converted
        /// </summary>
        public static IEnumerable<T> ReadMultiple<T>(TrakHoundTableResult tableResult)
        {
            if (tableResult == null || tableResult.Rows.IsNullOrEmpty()) return default;

            return TrakHoundTableReader.Read<T>(tableResult).ToList();
        }
    }
}
