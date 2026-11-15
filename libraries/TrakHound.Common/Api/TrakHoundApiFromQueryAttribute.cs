// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Binds an Endpoint Method Parameter to a Query Parameter of the Request
    /// </summary>
    public class FromQueryAttribute : Attribute
    {
        /// <summary>
        /// The name of the Query Parameter to bind (the Method Parameter Name is used when empty)
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// A description of the Parameter (shown in the Api Information, the OpenAPI Document, and MCP Tool inputs)
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// Binds the Query Parameter with the same name as the Method Parameter
        /// </summary>
        public FromQueryAttribute() { }

        /// <summary>
        /// Binds the Query Parameter with the specified name
        /// </summary>
        public FromQueryAttribute(string name)
        {
            ParameterName = name;
        }
    }
}
