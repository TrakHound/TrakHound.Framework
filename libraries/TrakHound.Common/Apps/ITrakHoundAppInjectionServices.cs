// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Apps
{
    /// <summary>
    /// Resolves App Injection Services for App Components, using the Transient, Scoped, or Singleton lifetime of the requested Service Type
    /// </summary>
    public interface ITrakHoundAppInjectionServices : IDisposable
    {
        /// <summary>
        /// Gets the Service of type <typeparamref name="TService"/> for the specified App (null if not found)
        /// </summary>
        TService Get<TService>(string appId) where TService : class, ITrakHoundAppInjectionService;

        /// <summary>
        /// Gets the Service of the specified Type for the specified App (null if not found)
        /// </summary>
        object Get(string appId, Type serviceType);
    }
}
