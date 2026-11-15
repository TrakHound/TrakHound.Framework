// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Logging
{
    /// <summary>
    /// Handles a Log Entry written by the specified Logger
    /// </summary>
    public delegate void TrakHoundLogEventHandler(ITrakHoundLogger logger, TrakHoundLogItem entry);
}
