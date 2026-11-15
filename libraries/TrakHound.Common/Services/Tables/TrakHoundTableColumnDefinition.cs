// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The definition of a Table Column, independent of any database
    /// </summary>
    public class TrakHoundTableColumnDefinition
    {
        /// <summary>
        /// The Length that means no limit (ex. nvarchar(max) on SQL Server)
        /// </summary>
        public const int MaxLength = -1;


        /// <summary>
        /// The name of the Column
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Data Type of the Column
        /// </summary>
        [JsonPropertyName("dataType")]
        [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTableDataType>))]
        public TrakHoundTableDataType DataType { get; set; }

        /// <summary>
        /// The length of a String or Binary Column (MaxLength for no limit, null for other types)
        /// </summary>
        [JsonPropertyName("length")]
        public int? Length { get; set; }

        /// <summary>
        /// True if a String Column always stores its full Length
        /// </summary>
        [JsonPropertyName("fixedLength")]
        public bool FixedLength { get; set; }

        /// <summary>
        /// True if a String Column must keep any Unicode character (databases that only store Unicode ignore this)
        /// </summary>
        [JsonPropertyName("unicode")]
        public bool Unicode { get; set; }

        /// <summary>
        /// The total number of digits of a Decimal Column
        /// </summary>
        [JsonPropertyName("precision")]
        public int? Precision { get; set; }

        /// <summary>
        /// The number of digits after the decimal point of a Decimal Column
        /// </summary>
        [JsonPropertyName("scale")]
        public int? Scale { get; set; }

        /// <summary>
        /// True if the Column allows null
        /// </summary>
        [JsonPropertyName("allowNull")]
        public bool AllowNull { get; set; }

        /// <summary>
        /// True if the Column is part of the Primary Key
        /// </summary>
        [JsonPropertyName("isPrimaryKey")]
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// The sort order of the Column in the Primary Key (null uses the database default)
        /// </summary>
        [JsonPropertyName("primaryKeyOrder")]
        public TrakHoundSortOrder? PrimaryKeyOrder { get; set; }

        /// <summary>
        /// True if a change to this Column causes a Merge to update the row (when any Column of the Table sets this,
        /// rows where none of these Columns changed are left alone)
        /// </summary>
        [JsonPropertyName("isUpdate")]
        public bool IsUpdate { get; set; }
    }
}
