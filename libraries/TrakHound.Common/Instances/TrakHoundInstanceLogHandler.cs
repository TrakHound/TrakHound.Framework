// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Instances
{
    /// <summary>
    /// Handles a Log Item received from an Instance
    /// </summary>
    public delegate void TrakHoundInstanceLogHandler(string instanceId, TrakHoundLogItem item);
}
