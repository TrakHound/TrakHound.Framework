// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Session for a User that was authenticated by an Identity Provider
    /// </summary>
    public sealed class TrakHoundAuthenticatedSession : ITrakHoundAuthenticatedSession
    {
        /// <summary>
        /// The unique Identifier of the Session
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// The Id of the Identity Provider that created the Session
        /// </summary>
        public string ProviderId { get; set; }

        /// <summary>
        /// The User the Session was authenticated for
        /// </summary>
        public ITrakHoundIdentityUser User { get; set; }

        /// <summary>
        /// The Roles of the Session. After Authentication by the Security Manager these are the Permissions mapped from the matching Assignments
        /// </summary>
        public IEnumerable<string> Roles { get; set; }

        /// <summary>
        /// The time the Session becomes valid
        /// </summary>
        public DateTime ValidFrom { get; set; }

        /// <summary>
        /// The time the Session expires
        /// </summary>
        public DateTime ValidTo { get; set; }
    }
}
