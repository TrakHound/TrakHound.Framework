// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// Information about the App that an App Injection Service belongs to
    /// </summary>
    public interface ITrakHoundAppInjectionServiceInformation
    {
        /// <summary>
        /// The ID of the App (App Configuration) that the Service belongs to
        /// </summary>
        string AppId { get; }

        /// <summary>
        /// The Configuration of the App that the Service belongs to
        /// </summary>
        ITrakHoundAppConfiguration Configuration { get; }
    }
}
