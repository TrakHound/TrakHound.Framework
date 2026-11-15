// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Modules
{
    /// <summary>
    /// An expected reason a Module could not be loaded (ex. the Assembly is missing or contains no Module Types)
    /// </summary>
    public class TrakHoundModuleLoadException : Exception
    {
        /// <summary>
        /// Create a new exception with a message describing why the Module could not be loaded
        /// </summary>
        public TrakHoundModuleLoadException(string message) : base(message) { }
    }
}
