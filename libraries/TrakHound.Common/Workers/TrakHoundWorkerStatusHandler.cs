// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Workers
{
    /// <summary>
    /// Handles a Status reported by a Worker
    /// </summary>
    public delegate void TrakHoundWorkerStatusHandler(string workerId, TrakHoundWorkerStatusType status);
}
