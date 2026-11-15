// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Session that is not associated with an authenticated User
    /// </summary>
    public sealed class TrakHoundAnnonymousSession : ITrakHoundAnnonymousSession
    {
        /// <summary>
        /// The unique Identifier of the Session
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// The Id of the Identity Provider that created the Session (null if no Provider was used)
        /// </summary>
        public string ProviderId { get; set; }

        /// <summary>
        /// The Roles of the Session. After Authentication by the Security Manager these are the Permissions mapped from the matching Assignments ("*" matches any)
        /// </summary>
        public IEnumerable<string> Roles { get; set; }
    }
}
