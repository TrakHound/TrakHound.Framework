// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Modules
{
    /// <summary>
    /// A collection of Assembly Load Contexts that Modules are loaded into (one per Package Version)
    /// </summary>
    public class TrakHoundModuleContext
    {
        private readonly Dictionary<string, TrakHoundModuleLoadContext> _contexts = new Dictionary<string, TrakHoundModuleLoadContext>();
        private readonly object _lock = new object();
        private readonly string _contextId;


        /// <summary>
        /// The unique Identifier of the Context
        /// </summary>
        public string Id => _contextId;


        /// <summary>
        /// Create a new Context with the specified Id (a new Guid is used if empty)
        /// </summary>
        public TrakHoundModuleContext(string contextId = null)
        {
            _contextId = !string.IsNullOrEmpty(contextId) ? contextId : Guid.NewGuid().ToString();
        }


        /// <summary>
        /// Gets the Load Context with the specified Id, creating a collectible one that resolves dependencies relative to the assemblyPath if it doesn't exist. Returns null if either argument is empty
        /// </summary>
        public TrakHoundModuleLoadContext AddLoadContext(string loadContextId, string assemblyPath)
        {
            if (!string.IsNullOrEmpty(loadContextId) && !string.IsNullOrEmpty(assemblyPath))
            {
                lock (_lock)
                {
                    if (!_contexts.ContainsKey(loadContextId))
                    {
                        var context = new TrakHoundModuleLoadContext(loadContextId, assemblyPath, true);
                        _contexts.Add(loadContextId, context);

                        return context;
                    }
                    else
                    {
                        return _contexts.GetValueOrDefault(loadContextId);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Load Context with the specified Id (null if not found)
        /// </summary>
        public TrakHoundModuleLoadContext GetLoadContext(string loadContextId)
        {
            if (!string.IsNullOrEmpty(loadContextId))
            {
                lock (_lock)
                {
                    if (_contexts.ContainsKey(loadContextId))
                    {
                        return _contexts.GetValueOrDefault(loadContextId);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Removes the Load Context with the specified Id and Unloads it (the Assemblies are released once nothing references their Types)
        /// </summary>
        public void RemoveLoadContext(string loadContextId)
        {
            if (!string.IsNullOrEmpty(loadContextId))
            {
                TrakHoundModuleLoadContext context;
                lock (_lock)
                {
                    if (!_contexts.Remove(loadContextId, out context)) return;
                }

                // Assemblies are released once nothing references their Types
                if (context.IsCollectible) context.Unload();
            }
        }
    }
}
