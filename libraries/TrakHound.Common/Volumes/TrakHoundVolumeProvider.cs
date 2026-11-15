// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound.Volumes
{
    /// <summary>
    /// A Provider for Volumes stored on the local File System, where each Volume is a subdirectory of the base Path
    /// </summary>
    public class TrakHoundVolumeProvider : ITrakHoundVolumeProvider
    {
        private const string _defaultDirectoryName = "volumes";

        private readonly string _basePath;


        /// <summary>
        /// The directory the Volumes are stored in
        /// </summary>
        public string BasePath => _basePath;


        /// <summary>
        /// Initializes a new Provider using the specified base Path (defaults to a "volumes" directory in the application's base directory)
        /// </summary>
        public TrakHoundVolumeProvider(string basePath = null)
        {
            _basePath = !string.IsNullOrEmpty(basePath) ? basePath : Path.Combine(AppContext.BaseDirectory, _defaultDirectoryName);
        }


        /// <summary>
        /// Gets the Volume with the specified ID
        /// </summary>
        public ITrakHoundVolume GetVolume(string id)
        {
            if (!string.IsNullOrEmpty(_basePath) && !string.IsNullOrEmpty(id))
            {
                try
                {
                    var path = Path.Combine(_basePath, id);
                    //if (!Directory.Exists(_basePath)) Directory.CreateDirectory(path);

                    return new TrakHoundVolume(id, path);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Deletes the Volume with the specified ID, including all of its contents
        /// </summary>
        public bool DeleteVolume(string id)
        {
            if (!string.IsNullOrEmpty(_basePath) && !string.IsNullOrEmpty(id))
            {
                try
                {
                    var path = Path.Combine(_basePath, id);
                    if (Directory.Exists(_basePath)) Directory.Delete(path, true);

                    return true;
                }
                catch { }
            }

            return false;
        }
    }
}
