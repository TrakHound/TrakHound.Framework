// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Configurations
{
    /// <summary>
    /// How a Configuration in a Configuration Profile changed
    /// </summary>
    public enum TrakHoundConfigurationChangeType
    {
        /// <summary>
        /// Read when its Category was Loaded
        /// </summary>
        Loaded,

        /// <summary>
        /// Added after its Category was Loaded (ex. a new file, or added by the UI)
        /// </summary>
        Added,

        /// <summary>
        /// An existing Configuration changed (ex. its file was edited)
        /// </summary>
        Updated,

        /// <summary>
        /// Removed (ex. its file was deleted)
        /// </summary>
        Removed
    }
}
