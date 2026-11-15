// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Volumes
{
    /// <summary>
    /// The type of change that occurred to a File in a Volume
    /// </summary>
    public enum TrakHoundVolumeOnChangeType
    {
        /// <summary>
        /// The File was created
        /// </summary>
        Create,
        /// <summary>
        /// The File was modified or renamed
        /// </summary>
        Modify,
        /// <summary>
        /// The File was deleted
        /// </summary>
        Delete
    }

    /// <summary>
    /// Handler for a change to a File in a Volume (the path is relative to the Volume root)
    /// </summary>
    public delegate void TrakHoundVolumeOnChangeHandler(string path, TrakHoundVolumeOnChangeType changeType);

    /// <summary>
    /// Interface for a Listener that monitors a Volume for changes to Files
    /// </summary>
    public interface ITrakHoundVolumeListener : IDisposable
    {
        /// <summary>
        /// Raised when a File is created, modified, or deleted
        /// </summary>
        event TrakHoundVolumeOnChangeHandler Changed;

        /// <summary>
        /// Starts listening for changes
        /// </summary>
        void Start();

        /// <summary>
        /// Stops listening for changes
        /// </summary>
        void Stop();
    }
}
