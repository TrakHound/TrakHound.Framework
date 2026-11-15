// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Thrown when the signed in Admin doesn't have the Permission for what was called
    /// </summary>
    public class TrakHoundAdminAccessException : Exception
    {
        public string ResourceId { get; }

        public string Permission { get; }


        public TrakHoundAdminAccessException(string resourceId, string permission, string message = null) : base(message ?? TrakHoundAdminSecurity.DeniedMessage)
        {
            ResourceId = resourceId;
            Permission = permission;
        }
    }

    /// <summary>
    /// The Admin Account making changes in the current async flow (an event in an Admin Interface Circuit, set by the
    /// TrakHoundAuditCircuitHandler). The code that makes a change checks it, so a Page that doesn't hide an action still can't perform it.
    /// </summary>
    /// <remarks>
    /// When there is no Current context the call isn't governed (ex. startup, a Module, or a System API Request, which Security Profiles authorize)
    /// </remarks>
    public sealed class TrakHoundAdminContext
    {
        private static readonly AsyncLocal<TrakHoundAdminContext> _current = new AsyncLocal<TrakHoundAdminContext>();
        private static readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundAdminContext>();


        public static TrakHoundAdminContext Current => _current.Value;

        /// <summary>
        /// Null when the Session is no longer valid (nothing is allowed)
        /// </summary>
        public string AccountId { get; }

        public string Username { get; }

        /// <summary>
        /// The Roles the Admin holds, each for everything or only for certain Zones (see TrakHoundAdminScope)
        /// </summary>
        public IReadOnlyList<(TrakHoundAdminRole Role, IReadOnlyList<string> Zones)> Assignments { get; }

        public IReadOnlyList<TrakHoundAdminRole> Roles { get; }


        public TrakHoundAdminContext(TrakHoundAdminAccount account)
        {
            AccountId = account?.Id;
            Username = account?.Username;
            Assignments = GetAssignments(account);
            Roles = Assignments.Select(o => o.Role).ToList();
        }

        public TrakHoundAdminContext(string username, IEnumerable<TrakHoundAdminRole> roles)
        {
            Username = username;
            Assignments = (roles ?? Enumerable.Empty<TrakHoundAdminRole>()).Where(o => o != null).Select(o => (o, (IReadOnlyList<string>)Array.Empty<string>())).ToList();
            Roles = Assignments.Select(o => o.Role).ToList();
        }

        public TrakHoundAdminContext(string username, IEnumerable<(TrakHoundAdminRole Role, IEnumerable<string> Zones)> assignments)
        {
            Username = username;
            Assignments = (assignments ?? Enumerable.Empty<(TrakHoundAdminRole, IEnumerable<string>)>()).Where(o => o.Item1 != null).Select(o => (o.Item1, NormalizeZones(o.Item2))).ToList();
            Roles = Assignments.Select(o => o.Role).ToList();
        }


        /// <summary>
        /// The Roles an Account holds (Roles that no longer exist are left out, and a disabled Account has none)
        /// </summary>
        public static IReadOnlyList<(TrakHoundAdminRole Role, IReadOnlyList<string> Zones)> GetAssignments(TrakHoundAdminAccount account)
        {
            var assignments = new List<(TrakHoundAdminRole, IReadOnlyList<string>)>();
            if (account != null && account.Enabled && account.Assignments != null)
            {
                foreach (var assignment in account.Assignments)
                {
                    var role = TrakHoundAdminSecurity.GetRole(assignment?.RoleId);
                    if (role != null) assignments.Add((role, NormalizeZones(assignment.Zones)));
                }
            }

            return assignments;
        }

        /// <summary>
        /// The Roles an Account holds (Roles that no longer exist are left out)
        /// </summary>
        public static IReadOnlyList<TrakHoundAdminRole> GetRoles(TrakHoundAdminAccount account) => GetAssignments(account).Select(o => o.Role).ToList();

        private static IReadOnlyList<string> NormalizeZones(IEnumerable<string> zones)
        {
            return zones?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        // A Role held for certain Zones only gives the Resources that are about a Module
        private static bool Gives((TrakHoundAdminRole Role, IReadOnlyList<string> Zones) assignment, string resourceId, string permission)
        {
            return assignment.Role.Allows(resourceId, permission) && (assignment.Zones.Count == 0 || TrakHoundAdminScope.IsScopeable(resourceId));
        }

        /// <summary>
        /// Whether the Admin has the Permission on the Resource everywhere (from a Role that isn't limited to Zones)
        /// </summary>
        public bool Can(string resourceId, string permission)
        {
            return Assignments.Any(o => o.Zones.Count == 0 && o.Role.Allows(resourceId, permission));
        }

        /// <summary>
        /// Whether the Admin has the Permission on the Resource for the target : everywhere, or from a Role that is limited to Zones the
        /// target is within
        /// </summary>
        public bool Can(string resourceId, string permission, TrakHoundAdminTarget target)
        {
            if (Can(resourceId, permission)) return true;
            if (target == null) return false;

            return Assignments.Any(o => o.Zones.Count > 0 && Gives(o, resourceId, permission) && TrakHoundAdminScope.IsInZones(o.Zones, target));
        }

        /// <summary>
        /// Whether the Admin has the Permission on the Resource for at least something (everywhere, or within some Zones). For showing an
        /// area or a page : what they do there is checked against what it is done to
        /// </summary>
        public bool CanSome(string resourceId, string permission)
        {
            return Assignments.Any(o => Gives(o, resourceId, permission));
        }

        /// <summary>
        /// The Zones the Admin's Roles that give the Permission on the Resource are limited to (empty when one of them isn't limited)
        /// </summary>
        public IReadOnlyList<string> GetZones(string resourceId, string permission)
        {
            if (Can(resourceId, permission)) return Array.Empty<string>();

            return Assignments.Where(o => o.Zones.Count > 0 && Gives(o, resourceId, permission)).SelectMany(o => o.Zones).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }


        /// <summary>
        /// Whether the Admin can give a User or API Key the Role (or change one that has it) : one of the Admin's Roles that allows
        /// changing the Resource (Users or API Keys) must be able to give it (see TrakHoundAdminRole.GrantableRoles)
        /// </summary>
        public bool CanGiveRole(string resourceId, string role)
        {
            // Users and API Keys aren't about a Module, so a Role that is limited to Zones doesn't count
            return Assignments.Any(o => o.Zones.Count == 0 && o.Role.Allows(resourceId, TrakHoundAdminSecurity.WritePermission) && o.Role.CanGive(role));
        }

        /// <summary>
        /// Returns why the current Admin can't give the Roles to a User or API Key, or change one that has them (null when they can)
        /// </summary>
        public static string GetGrantDenial(string resourceId, IEnumerable<string> roles)
        {
            var context = _current.Value;
            if (context == null || roles == null) return null;

            var denied = roles.FirstOrDefault(o => !context.CanGiveRole(resourceId, o));
            return denied != null ? $"{TrakHoundAdminSecurity.GrantableRoleDeniedMessage} : {denied}" : null;
        }

        /// <summary>
        /// Whether the current Admin has the Permission on the Resource (true when the call isn't governed)
        /// </summary>
        public static bool IsAllowed(string resourceId, string permission)
        {
            var context = _current.Value;
            return context == null || context.Can(resourceId, permission);
        }

        /// <summary>
        /// Returns why the current Admin can't do something (null when they can), for methods that return the reason a change wasn't made
        /// </summary>
        public static string GetDenial(string resourceId, string permission)
        {
            if (IsAllowed(resourceId, permission)) return null;

            _logger.LogWarning($"Admin Access Denied : {_current.Value?.Username} : {resourceId} ({permission})");
            return TrakHoundAdminSecurity.DeniedMessage;
        }

        /// <summary>
        /// Throws if the current Admin doesn't have the Permission on the Resource
        /// </summary>
        public static void Demand(string resourceId, string permission)
        {
            if (GetDenial(resourceId, permission) != null) throw new TrakHoundAdminAccessException(resourceId, permission);
        }

        /// <summary>
        /// Throws if the current Admin doesn't have "write" on the Resource
        /// </summary>
        public static void DemandWrite(string resourceId) => Demand(resourceId, TrakHoundAdminSecurity.WritePermission);

        /// <summary>
        /// Whether the current Admin has the Permission on the Resource for the target (true when the call isn't governed)
        /// </summary>
        public static bool IsAllowed(string resourceId, string permission, TrakHoundAdminTarget target)
        {
            var context = _current.Value;
            return context == null || context.Can(resourceId, permission, target);
        }

        /// <summary>
        /// Returns why the current Admin can't do something to the target (null when they can)
        /// </summary>
        public static string GetDenial(string resourceId, string permission, TrakHoundAdminTarget target)
        {
            if (IsAllowed(resourceId, permission, target)) return null;

            var context = _current.Value;
            _logger.LogWarning($"Admin Access Denied : {context?.Username} : {resourceId} ({permission}) : {target?.MemberId ?? target?.ZoneResourceId}");

            // Allowed for some Modules but not this one
            return context != null && context.CanSome(resourceId, permission) ? TrakHoundAdminSecurity.ZoneDeniedMessage : TrakHoundAdminSecurity.DeniedMessage;
        }

        /// <summary>
        /// Throws if the current Admin doesn't have the Permission on the Resource for the target
        /// </summary>
        public static void Demand(string resourceId, string permission, TrakHoundAdminTarget target)
        {
            var denial = GetDenial(resourceId, permission, target);
            if (denial != null) throw new TrakHoundAdminAccessException(resourceId, permission, denial);
        }


        public static IDisposable Begin(TrakHoundAdminContext context)
        {
            var previous = _current.Value;
            _current.Value = context;
            return new Scope(previous);
        }


        private sealed class Scope : IDisposable
        {
            private readonly TrakHoundAdminContext _previous;

            public Scope(TrakHoundAdminContext previous) => _previous = previous;

            public void Dispose() => _current.Value = _previous;
        }
    }
}
