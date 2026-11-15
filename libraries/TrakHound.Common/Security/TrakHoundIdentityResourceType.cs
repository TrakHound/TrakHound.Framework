// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Security
{
    /// <summary>
    /// The type of a Resource that can be secured
    /// </summary>
    public enum TrakHoundIdentityResourceType
    {
        /// <summary>
        /// An Api route
        /// </summary>
        Api,
        /// <summary>
        /// An App route
        /// </summary>
        App,
        /// <summary>
        /// An internal System route (ex. System Api Information)
        /// </summary>
        System,
        /// <summary>
        /// An Entity
        /// </summary>
        Entity,
        /// <summary>
        /// A Service operation (ex. reading a Blob or publishing a Message)
        /// </summary>
        Service
    }
}
