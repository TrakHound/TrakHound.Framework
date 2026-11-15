// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Controllers.Http
{
    /// <summary>
    /// Describes the Request Body of a System API action that reads the body itself (instead of binding a [FromBody] parameter)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public class RouteRequestBodyAttribute : Attribute
    {
        public string ContentType { get; set; }

        /// <summary>
        /// The Type of the Body (null for a Body described only by its Content Type, ex. a file)
        /// </summary>
        public Type BodyType { get; set; }

        public string Description { get; set; }

        public bool Required { get; set; } = true;


        public RouteRequestBodyAttribute(string contentType, Type bodyType = null)
        {
            ContentType = contentType;
            BodyType = bodyType;
        }
    }
}
