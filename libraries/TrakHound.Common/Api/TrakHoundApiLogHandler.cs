// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Api
{
    /// <summary>
    /// Handles a Log entry written by the Api with the specified ID
    /// </summary>
    public delegate void TrakHoundApiLogHandler(string apiId, TrakHoundLogItem item);
}
