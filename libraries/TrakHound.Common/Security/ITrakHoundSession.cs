// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Session created when a Request is Authenticated
    /// </summary>
    public interface ITrakHoundSession
    {
        /// <summary>
        /// The unique Identifier of the Session
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// The Id of the Identity Provider that created the Session (null if no Provider was used)
        /// </summary>
        public string ProviderId { get; }

        /// <summary>
        /// The Roles of the Session. After Authentication by the Security Manager these are the Permissions mapped from the matching Assignments ("*" matches any)
        /// </summary>
        IEnumerable<string> Roles { get; }
    }
}
