// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// What an Admin who can change Security Profiles, Users or API Keys is limited to, so that managing who can use Apis and Apps doesn't
    /// also let them hand out access to the Instance itself. Assignments that reach the System API or Services (see
    /// TrakHoundAdminSecurity.IsInstanceResource), and the Roles those Assignments apply to, need "write" on InstanceSecurityProfiles
    /// </summary>
    public static class TrakHoundAdminProfileRules
    {
        private const string _wildcard = "*";


        /// <summary>
        /// Throws if the signed in Admin can't change Profiles, or can't change these Assignments (the ones being saved, and the ones
        /// they replace or remove) because one reaches the System API or Services
        /// </summary>
        public static void DemandWrite(params IEnumerable<TrakHoundIdentityAssignment>[] assignments)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.SecurityProfiles);

            if (assignments != null && assignments.Any(o => o != null && o.Any(a => TrakHoundAdminSecurity.IsInstanceResource(a?.ResourceId))))
            {
                TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.InstanceSecurityProfiles);
            }
        }

        /// <summary>
        /// Whether an Identity from the Identity Provider with the Roles gets Permissions on a System Resource or a Service operation from
        /// any of the Profiles. True for an Identity without Roles when a Profile gives every Identity ("*") such Permissions
        /// </summary>
        public static bool ReachesInstance(IEnumerable<TrakHoundSecurityProfile> profiles, string providerId, IEnumerable<string> roles)
        {
            if (profiles == null) return false;

            foreach (var profile in profiles.Where(o => o != null && string.Equals(o.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
            {
                if (profile.Assignments == null) continue;

                foreach (var assignment in profile.Assignments)
                {
                    if (assignment != null && TrakHoundAdminSecurity.IsInstanceResource(assignment.ResourceId) && IsRoleMatch(assignment.Roles, roles)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The Role names that the Profiles using an Identity Provider give Permissions to (not Wildcards or patterns), and whether each
        /// reaches the System API or Services
        /// </summary>
        public static IReadOnlyList<(string Role, bool ReachesInstance)> GetRoles(IEnumerable<TrakHoundSecurityProfile> profiles, string providerId)
        {
            var list = profiles?.Where(o => o != null && string.Equals(o.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<TrakHoundSecurityProfile>();

            var roles = list
                .SelectMany(o => o.Assignments ?? Enumerable.Empty<TrakHoundIdentityAssignment>())
                .SelectMany(o => o?.Roles ?? Enumerable.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o) && !o.EndsWith(_wildcard))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase);

            return roles.Select(o => (o, ReachesInstance(list, providerId, new[] { o }))).ToList();
        }

        // The same matching the Security Manager uses to authorize : a Wildcard Role matches any Identity (including one without Roles),
        // and a Role ending with a Wildcard matches by prefix
        private static bool IsRoleMatch(IEnumerable<string> assignmentRoles, IEnumerable<string> identityRoles)
        {
            if (assignmentRoles == null) return false;

            foreach (var assignmentRole in assignmentRoles)
            {
                if (string.IsNullOrEmpty(assignmentRole)) continue;
                if (assignmentRole == _wildcard) return true;
                if (identityRoles == null) continue;

                foreach (var identityRole in identityRoles)
                {
                    if (string.IsNullOrEmpty(identityRole)) continue;

                    if (assignmentRole.EndsWith(_wildcard))
                    {
                        if (identityRole.StartsWith(assignmentRole.TrimEnd('*'), StringComparison.Ordinal)) return true;
                    }
                    else if (string.Equals(assignmentRole, identityRole, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
