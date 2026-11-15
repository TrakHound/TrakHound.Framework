// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound.Variables
{
    /// <summary>
    /// A Provider for Variable Collections stored as JSON files on the local File System
    /// </summary>
    public class TrakHoundVariableProvider : ITrakHoundVariableProvider
    {
        private const string _defaultDirectoryName = "variables";

        private readonly string _basePath;


        /// <summary>
        /// The directory the Variables are stored in
        /// </summary>
        public string BasePath => _basePath;


        /// <summary>
        /// Initializes a new Provider using the specified base Path (defaults to a "variables" directory in the application's base directory)
        /// </summary>
        public TrakHoundVariableProvider(string basePath = null)
        {
            _basePath = !string.IsNullOrEmpty(basePath) ? basePath : Path.Combine(AppContext.BaseDirectory, _defaultDirectoryName);
        }

        /// <summary>
        /// Gets the Variable Collection with the specified ID
        /// </summary>
        public ITrakHoundVariableCollection GetCollection(string id)
        {
            if (!string.IsNullOrEmpty(_basePath) && !string.IsNullOrEmpty(id))
            {
                try
                {
                    return new TrakHoundVariableCollection(id, _basePath);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Deletes the Variable Collection with the specified ID, including all of its Variables
        /// </summary>
        public bool DeleteCollection(string id)
        {
            if (!string.IsNullOrEmpty(_basePath) && !string.IsNullOrEmpty(id))
            {
                try
                {
                    var path = Path.Combine(_basePath, id);
                    if (File.Exists(_basePath)) File.Delete(path);

                    return true;
                }
                catch { }
            }

            return false;
        }
    }
}
