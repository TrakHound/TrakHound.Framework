// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The data type of a Table Column
    /// </summary>
    public enum TrakHoundTableDataType
    {
        /// <summary>
        /// Text
        /// </summary>
        String,
        /// <summary>
        /// 8-bit unsigned integer
        /// </summary>
        Byte,
        /// <summary>
        /// 16-bit integer
        /// </summary>
        Int16,
        /// <summary>
        /// 32-bit integer
        /// </summary>
        Int32,
        /// <summary>
        /// 64-bit integer
        /// </summary>
        Int64,
        /// <summary>
        /// Floating point number
        /// </summary>
        Float,
        /// <summary>
        /// Date and time
        /// </summary>
        Timestamp,
        /// <summary>
        /// Length of time
        /// </summary>
        Duration,
        /// <summary>
        /// True or false
        /// </summary>
        Boolean,
        /// <summary>
        /// Fixed point number (ex. decimal, money)
        /// </summary>
        Decimal,
        /// <summary>
        /// Unique identifier (Guid)
        /// </summary>
        Guid,
        /// <summary>
        /// Binary data
        /// </summary>
        Binary
    }
}
