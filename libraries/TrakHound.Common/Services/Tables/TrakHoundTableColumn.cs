// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Column of a Table or Query Result
    /// </summary>
    public class TrakHoundTableColumn
    {
        /// <summary>
        /// The name of the Column
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The UUID of the Definition associated with the Column (null if not set)
        /// </summary>
        [JsonPropertyName("definitionUuid")]
        public string DefinitionUuid { get; set; }

        /// <summary>
        /// The data type of the Column
        /// </summary>
        [JsonPropertyName("dataType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TrakHoundTableDataType DataType { get; set; }

        /// <summary>
        /// The maximum length of the Column's data (null if not applicable)
        /// </summary>
        [JsonPropertyName("dataLength")]
        public int? DataLength { get; set; }

        /// <summary>
        /// True if the Column's data is a fixed length (ex. char instead of varchar)
        /// </summary>
        [JsonPropertyName("fixedLength")]
        public bool FixedLength { get; set; }

        /// <summary>
        /// True if the Column allows null values
        /// </summary>
        [JsonPropertyName("allowNull")]
        public bool AllowNull { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundTableColumn
        /// </summary>
        public TrakHoundTableColumn() { }

        /// <summary>
        /// Creates a TrakHoundTableColumn with the specified name, data type and options
        /// </summary>
        public TrakHoundTableColumn(string name, TrakHoundTableDataType dataType, int? dataLength = null, bool fixedLength = false, bool allowNull = false)
        {
            Name = name;
            DataType = dataType;
            DataLength = dataLength;
            FixedLength = fixedLength;
            AllowNull = allowNull;
        }
    }
}
