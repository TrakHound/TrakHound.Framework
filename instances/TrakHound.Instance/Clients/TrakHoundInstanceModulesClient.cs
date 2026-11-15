// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Modules;

namespace TrakHound.Clients
{
    /// <summary>
    /// Reads the Modules directly from the Module Managers running in this process
    /// </summary>
    public class TrakHoundInstanceModulesClient : ITrakHoundModulesClient
    {
        private readonly ITrakHoundModuleProvider _moduleProvider;


        public TrakHoundInstanceModulesClient(ITrakHoundModuleProvider moduleProvider)
        {
            _moduleProvider = moduleProvider;
        }


        public Task<IEnumerable<TrakHoundModuleInformation>> GetModules()
        {
            return Task.FromResult(GetModules(_moduleProvider));
        }

        internal static IEnumerable<TrakHoundModuleInformation> GetModules(ITrakHoundModuleProvider moduleProvider)
        {
            var managers = moduleProvider?.GetManagers();
            if (managers == null) return Enumerable.Empty<TrakHoundModuleInformation>();

            return managers.SelectMany(o => o.GetInformation()).ToList();
        }
    }
}
