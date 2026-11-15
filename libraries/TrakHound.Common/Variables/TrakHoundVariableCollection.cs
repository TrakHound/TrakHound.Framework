// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace TrakHound.Variables
{
    /// <summary>
    /// A Collection of Variables stored as a JSON file ({id}.json) on the local File System
    /// </summary>
    public class TrakHoundVariableCollection : ITrakHoundVariableCollection
    {
        private readonly string _id;
        private readonly string _basePath;


        /// <summary>
        /// Gets the ID of the Collection
        /// </summary>
        public string Id => _id;


        /// <summary>
        /// Initializes a new Collection with the specified ID, stored in the specified base Path
        /// </summary>
        public TrakHoundVariableCollection(string id, string basePath)
        {
            _id = id;
            _basePath = basePath;
        }


        /// <summary>
        /// Sets the Variable with the specified Key to the specified Value, replacing any existing Value
        /// </summary>
        public async Task<bool> Set(string key, object value)
        {
            if (!string.IsNullOrEmpty(key) && value != null)
            {
                try
                {
                    if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);

                    var filePath = Path.Combine(_basePath, _id);
                    filePath = Path.ChangeExtension(filePath, "json");

                    var vars = await Json.ReadAsync<Dictionary<string, object>>(filePath);
                    if (vars == null) vars = new Dictionary<string, object>();

                    vars.Remove(key);
                    vars.Add(key, value);

                    await Json.WriteAsync(vars, filePath);

                    return true;
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Reads the Value of the Variable with the specified Key (null if not found)
        /// </summary>
        public async Task<object> Read(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    var filePath = Path.Combine(_basePath, _id);
                    filePath = Path.ChangeExtension(filePath, "json");

                    if (File.Exists(filePath))
                    {
                        var vars = await Json.ReadAsync<Dictionary<string, object>>(filePath);
                        if (!vars.IsNullOrEmpty())
                        {
                            return vars.GetValueOrDefault(key);
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the Value of the Variable with the specified Key as a string (null if not found)
        /// </summary>
        public async Task<string> ReadString(string key)
        {
            var value = await Read(key);
            if (value != null) return value.ToString();
            else return null;
        }
    }
}
