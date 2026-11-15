// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// A Resource that can be secured by the Security Manager (ex. an Api or App route)
    /// </summary>
    public sealed class TrakHoundIdentityResource
    {
        /// <summary>
        /// The unique Identifier of the Resource
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The type of Resource
        /// </summary>
        public TrakHoundIdentityResourceType Type { get; }

        /// <summary>
        /// The Permissions that apply to the Resource (null if none)
        /// </summary>
        public IEnumerable<string> Permissions { get; }


        /// <summary>
        /// Create a new Resource with the specified type, Id, and Permissions
        /// </summary>
        public TrakHoundIdentityResource(TrakHoundIdentityResourceType type, string id, IEnumerable<string> permissions = null)
        {
            Id = id;
            Type = type;
            Permissions = permissions;
        }
    }
}
