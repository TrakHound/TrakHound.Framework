// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Modules
{
    public class TrakHoundModuleProviderDebug : ITrakHoundModuleProvider
    {
        public TrakHoundModuleProviderDebug() { }


        public ITrakHoundModuleManager Get<TModule>(string packageCategory, TrakHoundModuleContext context = null, bool loadByDependencies = true)
        {
            return new TrakHoundModuleManagerDebug<TModule>();
        }

        public ITrakHoundModuleManager Get<TModule>(IEnumerable<string> packageCategories, TrakHoundModuleContext context = null, bool loadByDependencies = true)
        {
            return new TrakHoundModuleManagerDebug<TModule>();
        }

        public IEnumerable<ITrakHoundModuleManager> GetManagers() => Enumerable.Empty<ITrakHoundModuleManager>();
    }
}
