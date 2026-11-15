// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Session that is not associated with an authenticated User
    /// </summary>
    public interface ITrakHoundAnnonymousSession : ITrakHoundSession { }
}
