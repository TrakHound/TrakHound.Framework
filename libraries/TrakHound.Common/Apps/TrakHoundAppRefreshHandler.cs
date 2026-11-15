// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// Handles a request to Refresh the pages of an App or Client Connection (the ID is the App ID or Connection ID), with a requested Delay before Refreshing
    /// </summary>
    public delegate void TrakHoundAppRefreshHandler(string id, int delay);
}
