// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    /// <summary>
    /// Identifying information for a Service Router
    /// </summary>
    public interface ITrakHoundServiceRouterInformation
    {
        /// <summary>
        /// The unique Identifier of the Router
        /// </summary>
        string Id { get; }

        /// <summary>
        /// A friendly name for the Router
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the Router
        /// </summary>
        string Description { get; }
    }
}
