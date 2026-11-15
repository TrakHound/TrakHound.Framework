// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// An Identity verified by an Identity Provider (ex. a signed in User or an API Key)
    /// </summary>
    public sealed class TrakHoundIdentity
    {
        /// <summary>
        /// The ID of the Identity Provider that verified the Identity
        /// </summary>
        public string ProviderId { get; set; }

        /// <summary>
        /// The unique Identifier of the User (as given by the Identity Provider)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// The display name of the User
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Whether the Identity is the actual User (Origin) or an application acting on behalf of a User (Delegate)
        /// </summary>
        public TrakHoundIdentityUserType Type { get; set; }

        /// <summary>
        /// The Roles of the Identity (ex. Groups). Roles are mapped to Permissions by the Assignments of a Security Profile
        /// </summary>
        public IReadOnlyList<string> Roles { get; set; }

        /// <summary>
        /// Additional properties of the Identity (ex. email), keyed by name
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; set; }

        /// <summary>
        /// When the Identity expires (UTC). Null to use the Instance's Session lifetime
        /// </summary>
        public DateTime? Expires { get; set; }


        /// <summary>
        /// True if the Identity has an Expiration that has passed
        /// </summary>
        public bool IsExpired => Expires.HasValue && Expires.Value.ToUniversalTime() < DateTime.UtcNow;


        /// <summary>
        /// Converts the Identity to the User given to Apps through Sessions
        /// </summary>
        public ITrakHoundIdentityUser ToUser()
        {
            var user = new TrakHoundIdentityUser();
            user.Id = UserId;
            user.Type = Type;
            user.Source = ProviderId;

            var properties = new Dictionary<string, string>();
            if (Properties != null)
            {
                foreach (var property in Properties) properties[property.Key] = property.Value;
            }
            if (!string.IsNullOrEmpty(Name)) properties.TryAdd("name", Name);
            user.Properties = properties;

            return user;
        }
    }
}
