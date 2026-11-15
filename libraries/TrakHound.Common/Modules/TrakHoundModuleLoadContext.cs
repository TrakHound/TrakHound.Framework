// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Reflection;
using System.Runtime.Loader;

namespace TrakHound.Modules
{
    /// <summary>
    /// An Assembly Load Context that resolves a Module's dependencies (managed and unmanaged) from the Module's directory
    /// </summary>
    public class TrakHoundModuleLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;


        /// <summary>
        /// Create a new Load Context that resolves dependencies relative to the Assembly at modulePath. Collectible contexts can be Unloaded
        /// </summary>
        public TrakHoundModuleLoadContext(string contextId, string modulePath, bool isCollectible = false) : base(contextId, isCollectible)
        {
            _resolver = new AssemblyDependencyResolver(modulePath);
        }


        /// <summary>
        /// Resolves a dependency from the Module's directory (null to fall back to the default context)
        /// </summary>
        protected override Assembly Load(AssemblyName assemblyName)
        {
            string referencedAssemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (referencedAssemblyPath != null)
            {
                var assembly = LoadFromAssemblyPath(referencedAssemblyPath);
                if (assembly != null)
                {
                    return assembly;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a native library from the Module's directory (IntPtr.Zero to fall back to the default resolution)
        /// </summary>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
