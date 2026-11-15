// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Functions
{
    /// <summary>
    /// The kind of value a Function Parameter takes. Values are always passed as strings; the Type sets how they are entered and checked
    /// </summary>
    public enum TrakHoundFunctionParameterType
    {
        /// <summary>
        /// Any text
        /// </summary>
        String,

        /// <summary>
        /// A whole number (ex. 30)
        /// </summary>
        Integer,

        /// <summary>
        /// A number (ex. 2.5)
        /// </summary>
        Number,

        /// <summary>
        /// true or false
        /// </summary>
        Boolean,

        /// <summary>
        /// A date and time (ISO 8601, ex. 2026-10-28T08:00:00Z)
        /// </summary>
        DateTime,

        /// <summary>
        /// A JSON value (ex. an object or an array)
        /// </summary>
        Json
    }
}
