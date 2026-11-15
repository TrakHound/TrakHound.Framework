// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using TrakHound.Logging;
using TrakHound.Packages;

namespace TrakHound.Modules
{
    /// <summary>
    /// Creates and caches Module Managers for a Module Type and set of Package Categories
    /// </summary>
    public class TrakHoundModuleProvider : ITrakHoundModuleProvider
    {
        private readonly TrakHoundPackageManager _packageManager;
        private readonly Dictionary<string, ITrakHoundModuleManager> _moduleManagers = new Dictionary<string, ITrakHoundModuleManager>();
        private readonly ITrakHoundLogger _logger;
        private object _lock = new object();


        /// <summary>
        /// Create a new Module Provider for the specified Package Manager (without logging)
        /// </summary>
        public TrakHoundModuleProvider(TrakHoundPackageManager packageManager) : this(packageManager, null) { }

        /// <summary>
        /// Create a new Module Provider for the specified Package Manager. Managers log to the "modules" Logger
        /// </summary>
        public TrakHoundModuleProvider(TrakHoundPackageManager packageManager, ITrakHoundLogProvider logProvider)
        {
            _packageManager = packageManager;
            _logger = logProvider?.GetLoggerByName("modules");
        }


        /// <inheritdoc/>
        public ITrakHoundModuleManager Get<TModule>(string packageCategory, TrakHoundModuleContext context = null, bool loadByDependencies = true)
        {
            if (!string.IsNullOrEmpty(packageCategory))
            {
                var packageCategories = new string[] { packageCategory };
                return Get<TModule>(packageCategories, context, loadByDependencies);
            }

            return null;
        }

        /// <inheritdoc/>
        public ITrakHoundModuleManager Get<TModule>(IEnumerable<string> packageCategories, TrakHoundModuleContext context = null, bool loadByDependencies = true)
        {
            if (!packageCategories.IsNullOrEmpty())
            {
                var key = CreateKey<TModule>(packageCategories);
                if (key != null)
                {
                    // Check and create under the same lock so concurrent callers share one Manager
                    // (an extra Manager would stay subscribed to the Package Manager events)
                    lock (_lock)
                    {
                        var moduleManager = _moduleManagers.GetValueOrDefault(key);
                        if (moduleManager == null)
                        {
                            moduleManager = new TrakHoundModuleManager<TModule>(_packageManager, packageCategories, context: context, loadByDependencies: loadByDependencies)
                            {
                                Logger = _logger
                            };
                            _moduleManagers.Add(key, moduleManager);
                        }

                        return moduleManager;
                    }
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public IEnumerable<ITrakHoundModuleManager> GetManagers()
        {
            lock (_lock) return _moduleManagers.Values.ToList();
        }

        private string CreateKey<TModule>(IEnumerable<string> packageCategories)
        {
            if (!packageCategories.IsNullOrEmpty())
            {
                var moduleType = typeof(TModule);
                var moduleKey = $"{moduleType.Assembly.Location}:{moduleType.AssemblyQualifiedName}";

                var keys = new List<string>();
                keys.Add(moduleKey);
                keys.AddRange(packageCategories.OrderBy(o => o));
                return string.Join(":", keys).ToMD5Hash();
            }

            return null;
        }
    }
}
