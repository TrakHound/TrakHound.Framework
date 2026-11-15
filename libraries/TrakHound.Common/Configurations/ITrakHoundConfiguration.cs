// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Configurations
{
    /// <summary>
    /// A Configuration stored in a Configuration Profile (as a YAML file in its Category directory)
    /// </summary>
    public interface ITrakHoundConfiguration 
    {
        /// <summary>
        /// The unique Identifier of the Configuration (also used as the filename)
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The Category of the Configuration (ex. "worker"). Also the name of the directory it is stored in
        /// </summary>
        string Category { get; }

        /// <summary>
        /// The path of the file the Configuration was read from
        /// </summary>
        string Path { get; set; }

        /// <summary>
        /// A hash of the Configuration's contents (used to detect when it has changed)
        /// </summary>
        string Hash { get; }
    }
}
