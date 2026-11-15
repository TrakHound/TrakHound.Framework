// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using TrakHound.Instances;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// What the Admin signed in to a Blazor Circuit can see and change. Admin Interface Pages use it to hide what the Admin can't use.
    /// The Account's Roles are read from the store each time, so a change to them applies without signing in again
    /// </summary>
    public class AdminAccess
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundAdminSessions _sessions;
        private readonly AuthenticationStateProvider _authenticationStateProvider;


        public AdminAccess(ITrakHoundInstance instance, TrakHoundAdminSessions sessions, AuthenticationStateProvider authenticationStateProvider)
        {
            _instance = instance;
            _sessions = sessions;
            _authenticationStateProvider = authenticationStateProvider;
        }


        /// <summary>
        /// Everyone is an Owner when Admin Authentication is disabled
        /// </summary>
        public bool IsAuthenticationEnabled => _instance.Configuration == null || _instance.Configuration.AdminAuthenticationEnabled;

        /// <summary>
        /// The signed in Admin Account (null if not signed in, or Admin Authentication is disabled)
        /// </summary>
        public TrakHoundAdminAccount Account => _sessions.Validate(GetPrincipal());

        /// <summary>
        /// The Context for code that makes changes (null when calls aren't governed : Admin Authentication is disabled, or this isn't an Admin's Circuit)
        /// </summary>
        public TrakHoundAdminContext CreateContext()
        {
            if (!IsAuthenticationEnabled) return null;

            var principal = GetPrincipal();
            if (!TrakHoundAdminSessions.HasSession(principal)) return null;

            // A Session that is no longer valid gets a Context that allows nothing
            return new TrakHoundAdminContext(_sessions.Validate(principal));
        }

        /// <summary>
        /// Whether the Admin has the Permission on the Resource for at least something : everywhere, or (from a Role that is limited to
        /// Zones) within its Zones. What they then do is checked against what it is done to
        /// </summary>
        public bool Can(string resourceId, string permission)
        {
            if (!IsAuthenticationEnabled) return true;

            return new TrakHoundAdminContext(Account).CanSome(resourceId, permission);
        }

        /// <summary>
        /// Whether the Admin has the Permission on the Resource for the target (ex. a Module)
        /// </summary>
        public bool Can(string resourceId, string permission, TrakHoundAdminTarget target)
        {
            if (!IsAuthenticationEnabled) return true;

            return new TrakHoundAdminContext(Account).Can(resourceId, permission, target);
        }

        /// <summary>
        /// Whether the Admin can view a Module in the Explorer (ex. "function", and its Configuration ID)
        /// </summary>
        public bool CanViewModule(string type, string configurationId)
        {
            return Can(TrakHoundAdminSecurity.Modules, TrakHoundAdminSecurity.ReadPermission, TrakHoundAdminTarget.Module(type, configurationId));
        }

        /// <summary>
        /// Whether the Admin can view a Service's data in the Explorer : everywhere, or because a Rule of one of their Zones reaches the Service
        /// </summary>
        public bool CanViewService(string serviceConfigurationId)
        {
            if (!IsAuthenticationEnabled) return true;

            var context = new TrakHoundAdminContext(Account);
            if (context.Can(TrakHoundAdminSecurity.Services, TrakHoundAdminSecurity.ReadPermission)) return true;

            var zones = context.GetZones(TrakHoundAdminSecurity.Services, TrakHoundAdminSecurity.ReadPermission);
            return zones.Count > 0 && TrakHoundAdminScope.CouldReachService(zones, serviceConfigurationId);
        }

        /// <summary>
        /// Whether what the Admin can view of the Resource is limited to Zones (so lists of Modules are filtered)
        /// </summary>
        public bool IsLimitedToZones(string resourceId)
        {
            if (!IsAuthenticationEnabled) return false;

            var context = new TrakHoundAdminContext(Account);
            return !context.Can(resourceId, TrakHoundAdminSecurity.ReadPermission) && context.CanSome(resourceId, TrakHoundAdminSecurity.ReadPermission);
        }

        public bool CanRead(string resourceId) => Can(resourceId, TrakHoundAdminSecurity.ReadPermission);

        public bool CanWrite(string resourceId) => Can(resourceId, TrakHoundAdminSecurity.WritePermission);

        /// <summary>
        /// Whether the Admin can view any of the Resources
        /// </summary>
        public bool CanReadAny(params string[] resourceIds)
        {
            if (!IsAuthenticationEnabled) return true;

            var context = new TrakHoundAdminContext(Account);
            return resourceIds != null && resourceIds.Any(o => context.CanSome(o, TrakHoundAdminSecurity.ReadPermission));
        }

        /// <summary>
        /// Whether the Admin can open a Page with the Route's values (see GetPageResources)
        /// </summary>
        public bool CanOpen(Type pageType, IReadOnlyDictionary<string, object> routeValues)
        {
            var resourceIds = GetPageResources(pageType, routeValues);
            if (resourceIds == null) return true;
            if (!CanReadAny(resourceIds)) return false;

            // A Configuration's own page, for an Admin whose Role is limited to Zones : only a Module in one of them (a new one is checked when it is saved)
            if (routeValues != null && !routeValues.ContainsKey("WorkspaceId") &&
                routeValues.TryGetValue("CategoryKey", out var categoryKey) && routeValues.TryGetValue("ConfigurationId", out var configurationId) && configurationId != null)
            {
                var category = Pages.Configuration.ConfigurationCategories.Get(categoryKey?.ToString());
                var id = configurationId.ToString();
                if (category != null && !string.Equals(id, "new", StringComparison.OrdinalIgnoreCase))
                {
                    return Can(resourceIds[0], TrakHoundAdminSecurity.ReadPermission, TrakHoundAdminTarget.Module(category.ConfigurationCategory, id));
                }
            }

            return true;
        }

        /// <summary>
        /// Whether the Page that is open (set by InstanceRoutes) is one the Admin can view but not change. The Layout then hides the
        /// actions that change something (the elements with the "admin-write" class), which would only be refused
        /// </summary>
        public bool IsViewOnly { get; set; }

        /// <summary>
        /// Whether the Admin can view the Page but not change what it shows. Only for a Page with one Resource : a Page that needs one of
        /// several (ex. an overview) has parts for each, and a Page for every Admin (ex. Your Account) isn't limited
        /// </summary>
        public bool IsPageViewOnly(Type pageType, IReadOnlyDictionary<string, object> routeValues)
        {
            if (!IsAuthenticationEnabled) return false;

            var resourceIds = GetPageResources(pageType, routeValues);
            return resourceIds != null && resourceIds.Length == 1 && !CanWrite(resourceIds[0]);
        }

        /// <summary>
        /// The Admin Resources a Page needs "read" on one of (null when anyone who can reach the Admin Interface can open it):
        /// <list type="bullet">
        /// <item>A Deployment Workspace's pages (a "WorkspaceId" in the Route) are part of Deployments, whichever page it is</item>
        /// <item>The Configuration pages of a Category (a "CategoryKey" in the Route) need that Category's Resource</item>
        /// <item>Otherwise the Page's AdminResource attribute</item>
        /// </list>
        /// A Page that has none of these can't be opened by an Admin who isn't an Owner, so a new Page is never open by default
        /// </summary>
        public static string[] GetPageResources(Type pageType, IReadOnlyDictionary<string, object> routeValues)
        {
            if (pageType == null) return null;
            if (Attribute.IsDefined(pageType, typeof(AdminResourcePublicAttribute))) return null;

            if (routeValues != null)
            {
                if (routeValues.TryGetValue("WorkspaceId", out var workspaceId) && workspaceId != null) return new[] { TrakHoundAdminSecurity.Deployments };

                if (routeValues.TryGetValue("CategoryKey", out var categoryKey) && categoryKey != null)
                {
                    var category = Pages.Configuration.ConfigurationCategories.Get(categoryKey.ToString());
                    if (category != null) return new[] { TrakHoundAdminSecurity.GetConfigurationResource(category.ConfigurationCategory) };
                }
            }

            var attribute = (AdminResourceAttribute)Attribute.GetCustomAttribute(pageType, typeof(AdminResourceAttribute));
            if (attribute != null && !attribute.ResourceIds.IsNullOrEmpty()) return attribute.ResourceIds;

            return new[] { TrakHoundAdminSecurity.Admins };
        }


        private ClaimsPrincipal GetPrincipal()
        {
            try
            {
                // Already completed in a Circuit (it is set from the Request that started it)
                var task = _authenticationStateProvider.GetAuthenticationStateAsync();
                return task.IsCompletedSuccessfully ? task.Result?.User : null;
            }
            catch (InvalidOperationException)
            {
                return null; // The Authentication State is not available outside of a Circuit
            }
        }
    }
}
