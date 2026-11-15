// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instances
{
    /// <summary>
    /// Handles a change in an Instance's Status
    /// </summary>
    public delegate void TrakHoundInstanceStatusHandler(string instanceId, TrakHoundInstanceStatus status);
}
