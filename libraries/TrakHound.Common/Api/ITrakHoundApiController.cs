// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// A Controller that contains the Endpoints for an Api
    /// </summary>
    public interface ITrakHoundApiController 
    {
        /// <summary>
        /// The ID of the Api Configuration the Controller was loaded for
        /// </summary>
        string Id { get; set; }

        /// <summary>
        /// The ID of the Instance the Controller is running in
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// The base URL of the Instance's HTTP Interface
        /// </summary>
        string BaseUrl { get; }

        /// <summary>
        /// The Route of the Controller
        /// </summary>
        string BasePath { get; }

        /// <summary>
        /// The location of the Controller's Package files (relative to the Instance)
        /// </summary>
        string BaseLocation { get; }

        /// <summary>
        /// The Api Configuration the Controller was loaded for
        /// </summary>
        ITrakHoundApiConfiguration Configuration { get; }
    }
}
