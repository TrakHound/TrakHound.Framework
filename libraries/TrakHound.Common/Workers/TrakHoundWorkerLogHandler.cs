// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Workers
{
    /// <summary>
    /// Handles a Log Item written by a Worker
    /// </summary>
    public delegate void TrakHoundWorkerLogHandler(string workerId, TrakHoundLogItem item);
}
