using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// JSON request body used to send a Query along with its Parameters over HTTP
    /// </summary>
    public class TrakHoundTableQueryRequest
    {
        /// <summary>
        /// The Query to run
        /// </summary>
        [JsonPropertyName("query")]
        public string Query { get; set; }

        /// <summary>
        /// The Parameters passed with the Query (null if none)
        /// </summary>
        [JsonPropertyName("parameters")]
        public TrakHoundTableParameter[] Parameters { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundTableQueryRequest
        /// </summary>
        public TrakHoundTableQueryRequest() { }

        /// <summary>
        /// Creates a TrakHoundTableQueryRequest for the specified Query and Parameters (DBNull values are sent as null)
        /// </summary>
        public TrakHoundTableQueryRequest(string query, TrakHoundTableParameterCollection parameters)
        {
            Query = query;
            // DBNull doesn't serialize to JSON so send as null
            Parameters = parameters?.Select(o => new TrakHoundTableParameter
            {
                Name = o.Name,
                DataType = o.DataType,
                Value = o.Value is DBNull ? null : o.Value
            }).ToArray();
        }


        /// <summary>
        /// Gets the Parameters as a TrakHoundTableParameterCollection, converting JSON values back to CLR values
        /// using each Parameter's DataType (null if there are no Parameters)
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

        // Values deserialize as JsonElement so convert back to a CLR value using the DataType
        internal static object ConvertValue(TrakHoundTableDataType? dataType, object value)
        {
            if (value is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        if (dataType == TrakHoundTableDataType.Guid && Guid.TryParse(element.GetString(), out var guidValue)) return guidValue;
                        if (dataType == TrakHoundTableDataType.Binary) return Convert.FromBase64String(element.GetString());
                        return element.GetString();

                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;

                    case JsonValueKind.Number:
                        switch (dataType)
                        {
                            case TrakHoundTableDataType.Byte: return element.GetInt32();
                            case TrakHoundTableDataType.Int16: return element.GetInt32();
                            case TrakHoundTableDataType.Int32: return element.GetInt32();
                            case TrakHoundTableDataType.Boolean: return element.GetInt32();
                            case TrakHoundTableDataType.Int64: return element.GetInt64();
                            case TrakHoundTableDataType.Float: return element.GetDouble();
                            case TrakHoundTableDataType.Decimal: return element.GetDecimal(); // A double would lose precision
                        }
                        if (element.TryGetInt64(out var longValue)) return longValue;
                        return element.GetDouble();

                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        return null;

                    default: return element.GetRawText();
                }
            }

            return value;
        }
    }
}
