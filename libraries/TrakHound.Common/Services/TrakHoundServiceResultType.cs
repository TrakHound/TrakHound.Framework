// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    /// <summary>
    /// The type of result returned from a Service Request
    /// </summary>
    public enum TrakHoundServiceResultType
    {
        /// <summary>
        /// The Request succeeded
        /// </summary>
        Ok,
        /// <summary>
        /// The Request succeeded but returned no results
        /// </summary>
        Empty,
        /// <summary>
        /// The requested item was not found
        /// </summary>
        NotFound,
        /// <summary>
        /// The Service is not available
        /// </summary>
        NotAvailable,
        /// <summary>
        /// No Route is configured to handle the Request
        /// </summary>
        RouteNotConfigured,
        /// <summary>
        /// The Request was not valid
        /// </summary>
        BadRequest,
        /// <summary>
        /// The Request timed out
        /// </summary>
        Timeout,
        /// <summary>
        /// An error occurred while processing the Request
        /// </summary>
        InternalError
    }
}
