// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Modules
{
    /// <summary>
    /// Creates and caches Module Managers for a Module Type and set of Package Categories
    /// </summary>
    public interface ITrakHoundModuleProvider
    {
        /// <summary>
        /// Gets the Module Manager for TModule that loads from the specified Package Category, creating it if needed. When loadByDependencies is false, every DLL in the Package is scanned for Types
        /// </summary>
        ITrakHoundModuleManager Get<TModule>(string packageCategory, TrakHoundModuleContext context = null, bool loadByDependencies = true);

        /// <summary>
        /// Gets the Module Manager for TModule that loads from the specified Package Categories, creating it if needed. When loadByDependencies is false, every DLL in the Package is scanned for Types
        /// </summary>
        ITrakHoundModuleManager Get<TModule>(IEnumerable<string> packageCategories, TrakHoundModuleContext context = null, bool loadByDependencies = true);

        /// <summary>
        /// Gets all of the Module Managers that have been created
        /// </summary>
        IEnumerable<ITrakHoundModuleManager> GetManagers();
    }
}
