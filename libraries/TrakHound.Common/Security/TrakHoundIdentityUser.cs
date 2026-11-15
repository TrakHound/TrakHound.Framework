// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A User identified by an Identity Provider
    /// </summary>
    public class TrakHoundIdentityUser : ITrakHoundIdentityUser
    {
        /// <summary>
        /// The unique Identifier of the User (as given by the Identity Provider)
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Whether the User is the actual User (Origin) or an application acting on behalf of a User (Delegate)
        /// </summary>
        public TrakHoundIdentityUserType Type { get; set; }

        /// <summary>
        /// Where the User Identity came from (set by the Identity Provider)
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Additional properties of the User, keyed by name
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; set; }
    }
}
