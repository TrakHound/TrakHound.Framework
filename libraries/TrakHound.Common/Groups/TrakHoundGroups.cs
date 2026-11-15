// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;

namespace TrakHound.Groups
{
    public static class TrakHoundGroups
    {
        /// <summary>
        /// The Router a Module's Client uses: the Module's own RouterId, else its Group's RouterId, else null (the Default Router).
        /// A Zone Router still overrides this when the Client sends a Request.
        /// </summary>
        public static string GetRouterId(ITrakHoundConfigurationProfile configurationProfile, string routerId, string groupId)
        {
            if (!string.IsNullOrWhiteSpace(routerId)) return routerId.Trim();

            if (configurationProfile != null && !string.IsNullOrWhiteSpace(groupId))
            {
                var group = configurationProfile.Get<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory, groupId.Trim());
                if (!string.IsNullOrWhiteSpace(group?.RouterId)) return group.RouterId.Trim();
            }

            return null;
        }

        /// <summary>
        /// Identifies what a Module is loaded with : its Configuration (Hash) and the Router it resolves to.
        /// The Router is included so that a change to the Module's Group reloads it, while unrelated Group changes don't.
        /// </summary>
        public static string GetLoadHash(ITrakHoundConfigurationProfile configurationProfile, string configurationHash, string routerId, string groupId)
        {
            return $"{configurationHash}:{GetRouterId(configurationProfile, routerId, groupId)}";
        }
    }
}
