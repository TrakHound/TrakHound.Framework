// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Variables
{
    /// <summary>
    /// Interface for a Provider used to get and delete Variable Collections
    /// </summary>
    public interface ITrakHoundVariableProvider
    {
        /// <summary>
        /// Gets the Variable Collection with the specified ID
        /// </summary>
        ITrakHoundVariableCollection GetCollection(string id);

        /// <summary>
        /// Deletes the Variable Collection with the specified ID, including all of its Variables
        /// </summary>
        bool DeleteCollection(string id);
    }
}
