// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// An App Injection Service that is created once per Scope (Browser connection) and Disposed when the Scope ends
    /// </summary>
    public interface ITrakHoundScopedAppInjectionService : ITrakHoundAppInjectionService { }
}
