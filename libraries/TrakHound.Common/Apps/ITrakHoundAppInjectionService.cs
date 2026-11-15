// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// Base Interface for Services that are loaded from App Packages and injected into App Components
    /// (implement the Transient, Scoped, or Singleton Interface to set the Service's lifetime)
    /// </summary>
    public interface ITrakHoundAppInjectionService { }
}
