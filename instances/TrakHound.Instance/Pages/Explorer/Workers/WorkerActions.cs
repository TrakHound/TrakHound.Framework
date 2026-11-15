// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Workers;

namespace TrakHound.Instance.Pages.Explorer.Workers
{
    // When a Worker can be Started or Stopped (shared by the Workers list and the Worker page, so both enable the same buttons)
    public static class WorkerActions
    {
        // Start from a stopped (or failed) state
        public static bool CanStart(TrakHoundWorkerStatusType status)
        {
            return status == TrakHoundWorkerStatusType.Stopped || status == TrakHoundWorkerStatusType.Error;
        }

        // Stop while running (or retrying after an error)
        public static bool CanStop(TrakHoundWorkerStatusType status)
        {
            return status == TrakHoundWorkerStatusType.Started || status == TrakHoundWorkerStatusType.Starting || status == TrakHoundWorkerStatusType.Error;
        }
    }
}
