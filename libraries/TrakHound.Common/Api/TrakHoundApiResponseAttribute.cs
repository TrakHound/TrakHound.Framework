// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// Describes a Response an Endpoint can return (shown in the Api Information and OpenAPI Document)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TrakHoundApiResponseAttribute : Attribute
    {
        /// <summary>
        /// The HTTP Status Code of the Response
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The Type of the Response Content
        /// </summary>
        public Type ReturnType { get; set; }


        /// <summary>
        /// Describes a Response with the specified Status Code and Content Type
        /// </summary>
        public TrakHoundApiResponseAttribute(int statusCode, Type returnType)
        {
            StatusCode = statusCode;
            ReturnType = returnType;
        }
    }
}
