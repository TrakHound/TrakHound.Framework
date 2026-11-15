// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The severity level of a Log entry
    /// </summary>
    public enum TrakHoundLogLevel
    {
        /// <summary>
        /// A failure that requires immediate attention
        /// </summary>
        Critical,
        /// <summary>
        /// A failure of the current operation
        /// </summary>
        Error,
        /// <summary>
        /// An unexpected event that does not stop the operation
        /// </summary>
        Warning,
        /// <summary>
        /// General information about the operation
        /// </summary>
        Information,
        /// <summary>
        /// Information useful for debugging
        /// </summary>
        Debug,
        /// <summary>
        /// Detailed tracing information
        /// </summary>
        Trace
    }
}
