// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound.Api
{
    /// <summary>
    /// Binds an Endpoint Method Parameter to the Request Body
    /// </summary>
    public class FromBodyAttribute : Attribute 
    {
        /// <summary>
        /// The Content Type used to read the Request Body (the default for the Parameter Type is used when empty)
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// A description of the Parameter (shown in the Api Information, the OpenAPI Document, and MCP Tool inputs)
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// Binds the Request Body using the default Content Type for the Parameter Type
        /// </summary>
        public FromBodyAttribute() { }

        /// <summary>
        /// Binds the Request Body using the specified Content Type
        /// </summary>
        public FromBodyAttribute(string contentType)
        {
            ContentType = contentType;
        }


        /// <summary>
        /// Gets the default Content Type for a Parameter Type ("text/plain" for string, "application/octet-stream" for byte[] and Stream, otherwise "application/json")
        /// </summary>
        public static string GetDefaultContentType(Type type)
        {
            if (type == typeof(string)) return "text/plain";
            if (type == typeof(byte[])) return "application/octet-stream";
            if (type == typeof(Stream)) return "application/octet-stream";

            return "application/json";
        }
    }
}
