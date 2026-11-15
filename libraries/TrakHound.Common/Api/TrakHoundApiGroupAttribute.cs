// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Sets the Group Name used to organize an Endpoint in the Api Information and OpenAPI Document
    /// </summary>
    public class TrakHoundApiGroupAttribute : Attribute
    {
        /// <summary>
        /// The Group Name
        /// </summary>
        public string Name { get; set; }


        /// <summary>
        /// Sets the Group Name of the Endpoint
        /// </summary>
        public TrakHoundApiGroupAttribute(string name)
        {
            Name = name;
        }
    }
}
