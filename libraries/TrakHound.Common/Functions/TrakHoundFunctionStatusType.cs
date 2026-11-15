// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Functions
{
    /// <summary>
    /// The Status of a Function Run
    /// </summary>
    public enum TrakHoundFunctionStatusType
    {
        /// <summary>
        /// The Run has started
        /// </summary>
        Started,
        /// <summary>
        /// The Run is in progress
        /// </summary>
        Running,
        /// <summary>
        /// The Run was Cancelled
        /// </summary>
        Stopped,
        /// <summary>
        /// The Run completed and returned a Response
        /// </summary>
        Completed,
        /// <summary>
        /// The Run failed with an error
        /// </summary>
        Error,
        /// <summary>
        /// The Run is waiting
        /// </summary>
        Wait
    }
}
