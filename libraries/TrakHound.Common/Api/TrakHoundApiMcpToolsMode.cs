// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// Which Endpoints of an Api are served as Tools by the Instance's MCP Server
    /// </summary>
    public enum TrakHoundApiMcpToolsMode
    {
        /// <summary>
        /// None
        /// </summary>
        Off,

        /// <summary>
        /// The Endpoints the package marks with ApiMcpToolAttribute (the default for Api Configurations)
        /// </summary>
        Marked,

        /// <summary>
        /// Every Endpoint. Marked Endpoints use their ApiMcpToolAttribute; others are named by their Method and are read only when they are GET or Stream Endpoints
        /// </summary>
        All
    }
}
