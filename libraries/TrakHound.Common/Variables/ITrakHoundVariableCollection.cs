// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;

namespace TrakHound.Variables
{
    /// <summary>
    /// Interface for a Collection of Key/Value Variables
    /// </summary>
    public interface ITrakHoundVariableCollection
    {
        /// <summary>
        /// Gets the ID of the Collection
        /// </summary>
        string Id { get; }


        /// <summary>
        /// Sets the Variable with the specified Key to the specified Value, replacing any existing Value
        /// </summary>
        Task<bool> Set(string key, object value);

        /// <summary>
        /// Reads the Value of the Variable with the specified Key (null if not found)
        /// </summary>
        Task<object> Read(string key);

        /// <summary>
        /// Reads the Value of the Variable with the specified Key as a string (null if not found)
        /// </summary>
        Task<string> ReadString(string key);
    }
}
