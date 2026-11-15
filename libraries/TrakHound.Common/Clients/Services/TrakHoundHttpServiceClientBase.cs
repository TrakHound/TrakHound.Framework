// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Base class for the HTTP Service Clients
    /// </summary>
    public abstract class TrakHoundHttpServiceClientBase : TrakHoundHttpClientBase//, ITrakHoundServiceClient
    {
        /// <summary>
        /// Creates a new Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpServiceClientBase(TrakHoundHttpClient baseClient) : base(baseClient) { }
    }
}
