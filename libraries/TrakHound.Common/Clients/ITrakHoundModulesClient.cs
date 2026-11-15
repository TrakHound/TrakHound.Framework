// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Modules;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query the Modules loaded by a TrakHound Instance
    /// </summary>
    public interface ITrakHoundModulesClient
    {
        /// <summary>
        /// Gets the loaded Modules and the Modules that failed to load
        /// </summary>
        Task<IEnumerable<TrakHoundModuleInformation>> GetModules();
    }
}
