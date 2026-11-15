// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A User identified by an Identity Provider
    /// </summary>
    public interface ITrakHoundIdentityUser
    {
        /// <summary>
        /// The unique Identifier of the User (as given by the Identity Provider)
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Whether the User is the actual User (Origin) or an application acting on behalf of a User (Delegate)
        /// </summary>
        TrakHoundIdentityUserType Type { get; }

        /// <summary>
        /// Where the User Identity came from (set by the Identity Provider)
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Additional properties of the User, keyed by name
        /// </summary>
        IReadOnlyDictionary<string, string> Properties { get; }
    }
}
