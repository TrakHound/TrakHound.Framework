// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Binds an Endpoint Method Parameter to a Route Parameter (matched by the Method Parameter Name)
    /// </summary>
    public class FromRouteAttribute : Attribute
    {
        /// <summary>
        /// The name of the Route Parameter (currently not used for binding, Route Parameters are matched by the Method Parameter Name)
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// A description of the Parameter (shown in the Api Information, the OpenAPI Document, and MCP Tool inputs)
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// Binds the Route Parameter with the same name as the Method Parameter
        /// </summary>
        public FromRouteAttribute() { }

        /// <summary>
        /// Sets the name of the Route Parameter (currently not used for binding)
        /// </summary>
        public FromRouteAttribute(string name)
        {
            ParameterName = name;
        }
    }
}
