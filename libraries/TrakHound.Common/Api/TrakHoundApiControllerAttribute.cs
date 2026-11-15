// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Sets the Route, Name, and Description of an Api Controller
    /// </summary>
    public class TrakHoundApiControllerAttribute : Attribute
    {
        /// <summary>
        /// The Route of the Controller, relative to the Api Route ("/" when empty)
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// The display Name of the Controller (the Class Name is used when empty)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Controller
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// Sets the Route of the Controller with an optional Name and Description
        /// </summary>
        public TrakHoundApiControllerAttribute(string route, string name = null, string description = null)
        {
            Route = route;
            Name = name;
            Description = description;
        }
    }
}
