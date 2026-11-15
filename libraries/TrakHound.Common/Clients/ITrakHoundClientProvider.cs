// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Clients
{
    /// <summary>
    /// Provides ITrakHoundClient instances configured for a specific Service Router
    /// </summary>
    public interface ITrakHoundClientProvider
    {
        /// <summary>
        /// Gets a Client that uses the specified Service Router (null uses the default Router)
        /// </summary>
        ITrakHoundClient GetClient(string routerId = null);
    }
}
