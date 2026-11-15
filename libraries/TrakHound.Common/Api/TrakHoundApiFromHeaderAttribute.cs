// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Binds an Endpoint Method Parameter to a Header of the Request (Header names are case-insensitive).
    /// Headers that carry the caller's credentials for the Instance are withheld (see TrakHoundApiHeaders)
    /// </summary>
    public class FromHeaderAttribute : Attribute
    {
        /// <summary>
        /// The name of the Header to bind (the Method Parameter Name is used when empty)
        /// </summary>
        public string HeaderName { get; set; }

        /// <summary>
        /// A description of the Parameter (shown in the Api Information and the OpenAPI Document)
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// Binds the Header with the same name as the Method Parameter
        /// </summary>
        public FromHeaderAttribute() { }

        /// <summary>
        /// Binds the Header with the specified name
        /// </summary>
        public FromHeaderAttribute(string name)
        {
            HeaderName = name;
        }


        /// <summary>
        /// The name of the Header bound to the Parameter (the HeaderName, or the Method Parameter Name when it is empty)
        /// </summary>
        public static string GetHeaderName(FromHeaderAttribute attribute, string parameterName)
        {
            return !string.IsNullOrEmpty(attribute?.HeaderName) ? attribute.HeaderName : parameterName;
        }
    }
}
