// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Ordered steps for a Table Service to run. The Service generates or selects the SQL for its own database, so the module
    /// sending the Request doesn't need to know which database that is. See TrakHoundTableRequestBuilder
    /// </summary>
    public class TrakHoundTableRequest
    {
        /// <summary>
        /// The steps to run, in order
        /// </summary>
        [JsonPropertyName("steps")]
        public TrakHoundTableStep[] Steps { get; set; }

        /// <summary>
        /// The Parameters referenced by the Query steps as @name (null if none). Rows stay with their step
        /// </summary>
        [JsonPropertyName("parameters")]
        public TrakHoundTableParameter[] Parameters { get; set; }

        /// <summary>
        /// The timeout of each command, in seconds (null uses the Service's default)
        /// </summary>
        [JsonPropertyName("timeout")]
        public int? Timeout { get; set; }

        /// <summary>
        /// True if the steps run in one Transaction, so that either all of them apply or none do
        /// </summary>
        [JsonPropertyName("transaction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // Defaults to true, so false must be sent or it reads back as true
        public bool Transaction { get; set; } = true;

        /// <summary>
        /// True if the Request only reads, so it needs the "read" Permission instead of "write"
        /// </summary>
        [JsonPropertyName("readOnly")]
        public bool ReadOnly { get; set; }


        /// <summary>
        /// Creates a Request that reads with one Query step
        /// </summary>
        public static TrakHoundTableRequest Query(TrakHoundTableQueryVariants variants, TrakHoundTableParameterCollection parameters = null, int? skip = null, int? take = null, int? timeout = null)
        {
            var request = new TrakHoundTableRequest();
            request.Steps = new[] { new TrakHoundTableStep { Type = TrakHoundTableStepType.Query, Variants = variants, Skip = skip, Take = take } };
            request.Parameters = GetParameters(parameters);
            request.Timeout = timeout;
            request.Transaction = false;
            request.ReadOnly = true;
            return request;
        }

        /// <summary>
        /// Creates a Request that reads with one Query step written in the portable "ansi" subset of SQL
        /// </summary>
        public static TrakHoundTableRequest Ansi(string query, TrakHoundTableParameterCollection parameters = null, int? skip = null, int? take = null, int? timeout = null)
        {
            return Query(TrakHoundTableQueryVariants.Ansi(query), parameters, skip, take, timeout);
        }

        /// <summary>
        /// Creates a Request that changes data with one Query step written in the portable "ansi" subset of SQL
        /// </summary>
        public static TrakHoundTableRequest AnsiWrite(string query, TrakHoundTableParameterCollection parameters = null, int? timeout = null)
        {
            var request = Query(TrakHoundTableQueryVariants.Ansi(query), parameters, null, null, timeout);
            request.Transaction = true;
            request.ReadOnly = false;
            return request;
        }

        /// <summary>
        /// Creates a Request that runs a script. A script manages its own Transaction, and must be safe to run again since nothing records that it has run.
        /// Send it as its own Request: statements that already ran stay applied if a later one fails
        /// </summary>
        public static TrakHoundTableRequest Script(TrakHoundTableQueryVariants variants, int? timeout = null)
        {
            var request = new TrakHoundTableRequest();
            request.Steps = new[] { new TrakHoundTableStep { Type = TrakHoundTableStepType.Query, Variants = variants } };
            request.Timeout = timeout;
            request.Transaction = false;
            return request;
        }


        /// <summary>
        /// Gets the Parameters as a TrakHoundTableParameterCollection, converting JSON values back to CLR values
        /// using each Parameter's Data Type (null if there are no Parameters)
        /// </summary>
        public TrakHoundTableParameterCollection GetParameters()
        {
            if (!Parameters.IsNullOrEmpty())
            {
                var parameters = new TrakHoundTableParameterCollection();
                foreach (var parameter in Parameters)
                {
                    if (parameter != null)
                    {
                        parameters.Add(parameter.Name, parameter.DataType, ConvertValue(parameter.DataType, parameter.Value));
                    }
                }
                return parameters;
            }

            return null;
        }

        /// <summary>
        /// Gets the Parameters to send with a Request (DBNull values are sent as null)
        /// </summary>
        internal static TrakHoundTableParameter[] GetParameters(TrakHoundTableParameterCollection parameters)
        {
            return parameters?.Select(o => new TrakHoundTableParameter
            {
                Name = o.Name,
                DataType = o.DataType,
                Value = o.Value is System.DBNull ? null : o.Value
            }).ToArray();
        }

        /// <summary>
        /// Converts a value read from JSON back to a CLR value using its Data Type (other values are returned as they are)
        /// </summary>
        public static object ConvertValue(TrakHoundTableDataType? dataType, object value)
        {
            return TrakHoundTableQueryRequest.ConvertValue(dataType, value);
        }
    }
}
