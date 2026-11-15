// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Security
{
    /// <summary>
    /// Assigns Permissions on a Resource to Identities that have one of the specified Roles
    /// </summary>
    public class TrakHoundIdentityAssignment
    {
        /// <summary>
        /// The unique Identifier of the Assignment
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// A description of the Assignment
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Id of the Resource the Assignment applies to ("*" matches all, a trailing "*" matches by prefix)
        /// </summary>
        [JsonPropertyName("resourceId")]
        public string ResourceId { get; set; }

        /// <summary>
        /// The Roles (from the Identity Provider) the Assignment applies to ("*" matches all, a trailing "*" matches by prefix)
        /// </summary>
        [JsonPropertyName("roles")]
        public IEnumerable<string> Roles { get; set; }

        /// <summary>
        /// The Permissions granted to matching Roles
        /// </summary>
        [JsonPropertyName("permissions")]
        public IEnumerable<string> Permissions { get; set; }
    }
}
