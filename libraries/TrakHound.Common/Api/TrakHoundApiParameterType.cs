// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrakHound.Api
{
    /// <summary>
    /// Where an Endpoint Parameter is read from
    /// </summary>
    public enum TrakHoundApiParameterType
    {
        /// <summary>
        /// Read from the Request Route (FromRouteAttribute)
        /// </summary>
        Route,
        /// <summary>
        /// Read from the Request Query Parameters (FromQueryAttribute)
        /// </summary>
        Query,
        /// <summary>
        /// Read from the Request Body (FromBodyAttribute)
        /// </summary>
        Body,
        /// <summary>
        /// Read from the Request Headers (FromHeaderAttribute)
        /// </summary>
        Header
    }
}
