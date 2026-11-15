// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Deployments
{
    /// <summary>
    /// How an item in a Deployment Manifest changed compared to a previous Manifest
    /// </summary>
    public enum TrakHoundDeploymentChangeType
    {
        /// <summary>
        /// The item was removed
        /// </summary>
        Removed = -1,

        /// <summary>
        /// The item exists in both and was changed
        /// </summary>
        Modified = 0,

        /// <summary>
        /// The item was added
        /// </summary>
        Added = 1
    }
}
