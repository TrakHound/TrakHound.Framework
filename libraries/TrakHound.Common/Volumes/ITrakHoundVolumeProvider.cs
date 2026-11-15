// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Volumes
{
    /// <summary>
    /// Interface for a Provider used to get and delete Volumes
    /// </summary>
    public interface ITrakHoundVolumeProvider
    {
        /// <summary>
        /// Gets the Volume with the specified ID
        /// </summary>
        ITrakHoundVolume GetVolume(string id);

        /// <summary>
        /// Deletes the Volume with the specified ID, including all of its contents
        /// </summary>
        bool DeleteVolume(string id);
    }
}
