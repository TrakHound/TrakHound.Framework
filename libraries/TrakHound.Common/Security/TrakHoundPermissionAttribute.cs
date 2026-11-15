// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Security
{
    /// <summary>
    /// Specifies a Permission required to call an Api Endpoint method (can be applied multiple times)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class TrakHoundPermissionAttribute : Attribute
    {
        /// <summary>
        /// The name of the required Permission
        /// </summary>
        public string Permission { get; set; }


        /// <summary>
        /// Create a new attribute requiring the specified Permission
        /// </summary>
        public TrakHoundPermissionAttribute(string permission)
        {
            Permission = permission;
        }
    }
}
