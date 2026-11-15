// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Session for a User that was authenticated by an Identity Provider
    /// </summary>
    public interface ITrakHoundAuthenticatedSession : ITrakHoundSession
    {
        /// <summary>
        /// The User the Session was authenticated for
        /// </summary>
        ITrakHoundIdentityUser User { get; }

        /// <summary>
        /// The time the Session becomes valid
        /// </summary>
        DateTime ValidFrom { get; }

        /// <summary>
        /// The time the Session expires
        /// </summary>
        DateTime ValidTo { get; }
    }
}
