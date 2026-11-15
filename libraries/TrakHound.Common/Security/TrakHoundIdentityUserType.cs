// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Security
{
    /// <summary>
    /// The type of User identified by an Identity Provider
    /// </summary>
    public enum TrakHoundIdentityUserType
    {
        /// <summary>
        /// The actual User
        /// </summary>
        Origin,   // The actual User
        /// <summary>
        /// An application that has delegated access from a User
        /// </summary>
        Delegate  // An application that has delegate access from a User
    }
}
