// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;

namespace TrakHound.Apps
{
    /// <summary>
    /// Stores data in the Browser's Local Storage for an App
    /// </summary>
    public interface ITrakHoundAppLocalStorageService
    {
        /// <summary>
        /// Saves the data to Local Storage under the specified Key (replacing any existing value)
        /// </summary>
        ValueTask SetItem<TData>(string key, TData data);

        /// <summary>
        /// Reads the data stored in Local Storage under the specified Key (default if not found)
        /// </summary>
        ValueTask<TData> GetItem<TData>(string key);

        /// <summary>
        /// Removes the data stored in Local Storage under the specified Key
        /// </summary>
        ValueTask RemoveItem(string key);
    }
}
