// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Zones;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// An area of the Admin Interface (ex. "admin:packages") that Admin Roles give Permissions on
    /// </summary>
    public class TrakHoundAdminResource
    {
        public string Id { get; }

        public string Title { get; }

        public string Description { get; }


        public TrakHoundAdminResource(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    /// <summary>
    /// Permissions on the Admin Resources that match a pattern ("*" matches all, a trailing "*" matches by prefix)
    /// </summary>
    public class TrakHoundAdminGrant
    {
        public string Resource { get; set; }

        public List<string> Permissions { get; set; }


        public TrakHoundAdminGrant() { }

        public TrakHoundAdminGrant(string resource, params string[] permissions)
        {
            Resource = resource;
            Permissions = permissions.ToList();
        }
    }

    /// <summary>
    /// A named set of Permissions that Admin Accounts hold. An Account with several Roles has the Permissions of all of them
    /// </summary>
    public class TrakHoundAdminRole
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public List<TrakHoundAdminGrant> Grants { get; set; }

        /// <summary>
        /// The Roles (of Users and API Keys) an Admin with this Role can give, or change a User or Key that has them : names, or patterns
        /// ending with "*". Any Role when empty. Only applies when the Role allows changing Users or API Keys
        /// </summary>
        public List<string> GrantableRoles { get; set; }

        /// <summary>
        /// One of the Roles that are part of the Instance (not a Role an Owner added)
        /// </summary>
        [YamlDotNet.Serialization.YamlIgnore]
        public bool BuiltIn { get; set; }


        public TrakHoundAdminRole Clone()
        {
            var clone = (TrakHoundAdminRole)MemberwiseClone();
            clone.Grants = Grants?.Where(o => o != null).Select(o => new TrakHoundAdminGrant { Resource = o.Resource, Permissions = o.Permissions?.ToList() }).ToList();
            clone.GrantableRoles = GrantableRoles?.ToList();
            return clone;
        }

        /// <summary>
        /// Whether an Admin with this Role can give a User or API Key the Role (see GrantableRoles)
        /// </summary>
        public bool CanGive(string role)
        {
            if (GrantableRoles.IsNullOrEmpty()) return true;
            return !string.IsNullOrEmpty(role) && GrantableRoles.Any(o => TrakHoundZoneResources.IsPatternMatch(o, role));
        }

        /// <summary>
        /// Whether the Role allows the Permission on the Resource. "write" includes "read"
        /// </summary>
        public bool Allows(string resourceId, string permission)
        {
            if (string.IsNullOrEmpty(resourceId) || Grants == null) return false;

            foreach (var grant in Grants)
            {
                if (grant?.Permissions == null || !TrakHoundZoneResources.IsPatternMatch(grant.Resource, resourceId)) continue;

                foreach (var granted in grant.Permissions)
                {
                    if (granted == TrakHoundAdminSecurity.WritePermission || granted == TrakHoundZoneResources.Wildcard) return true;
                    if (string.Equals(granted, permission, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The Admin Resources ("admin:{area}") and the built in Admin Roles. Requests that only view require the "read" Permission and
    /// ones that change something require "write". These apply to the Admin Interface only : the System API and the MCP Server are
    /// authorized by Security Profiles (see TrakHoundSystemSecurity)
    /// </summary>
    public static class TrakHoundAdminSecurity
    {
        public const string ReadPermission = "read";
        public const string WritePermission = "write";

        public const string Instance = "admin:instance";
        public const string Modules = "admin:modules";
        public const string Services = "admin:services";
        public const string Volumes = "admin:volumes";
        public const string Logs = "admin:logs";
        public const string Packages = "admin:packages";
        public const string PackageSources = "admin:package-sources";
        public const string ConfigurationPrefix = "admin:configuration:";
        public const string Variables = "admin:variables";
        public const string Settings = "admin:settings";
        public const string Deployments = "admin:deployments";
        public const string IdentityProviders = "admin:identity-providers";
        public const string SecurityProfiles = "admin:security-profiles";
        public const string InstanceSecurityProfiles = "admin:security-profiles:instance";
        public const string Users = "admin:users";
        public const string ApiKeys = "admin:api-keys";
        public const string TrustedPublishers = "admin:trusted-publishers";
        public const string Zones = "admin:zones";
        public const string Audit = "admin:audit";
        public const string Licenses = "admin:licenses";
        public const string Admins = "admin:admins";

        public const string AppConfigurations = ConfigurationPrefix + "app";
        public const string ApiConfigurations = ConfigurationPrefix + "api";
        public const string FunctionConfigurations = ConfigurationPrefix + "function";
        public const string WorkerConfigurations = ConfigurationPrefix + "worker";
        public const string ServiceConfigurations = ConfigurationPrefix + "service";
        public const string RouterConfigurations = ConfigurationPrefix + "router";
        public const string GroupConfigurations = ConfigurationPrefix + "group";

        public const string OwnerRoleId = "owner";
        public const string PackagesRoleId = "packages";
        public const string SecurityOperatorRoleId = "security-operator";
        public const string ViewerRoleId = "viewer";

        public const string DeniedMessage = "Your Admin Account doesn't have permission to do this";
        public const string ZoneDeniedMessage = "Your Admin Account can only do this in its Zones, and this isn't in one of them";
        public const string GrantableRoleDeniedMessage = "Your Admin Account can't give this Role, or change a User or API Key that has it";
        public const string InstanceRoleDeniedMessage = "These Roles give access to the System API or Services, which your Admin Account can't give or change";


        /// <summary>
        /// The Resources of each area of the Admin Interface. An area is shown in the navigation when the Admin can view any of them
        /// </summary>
        public static readonly string[] ExplorerResources = new[] { Instance, Modules, Services, Logs };
        public static readonly string[] ConfigurationResources = new[] { AppConfigurations, ApiConfigurations, FunctionConfigurations, WorkerConfigurations, ServiceConfigurations, RouterConfigurations, GroupConfigurations, IdentityProviders, SecurityProfiles, Zones, Settings, Variables };
        public static readonly string[] SecurityResources = new[] { SecurityProfiles, Users, ApiKeys, IdentityProviders, TrustedPublishers, Audit, Admins };

        public static readonly IReadOnlyList<TrakHoundAdminResource> Resources = new[]
        {
            new TrakHoundAdminResource(Instance, "Instance", "The Instance overview, Monitor, Health and Diagnostics, and starting / stopping the Instance (write)"),
            new TrakHoundAdminResource(Modules, "Modules", "Apps, Apis, Functions and Workers in the Explorer, and running Functions, starting / stopping Workers and calling Api Endpoints (write)"),
            new TrakHoundAdminResource(Services, "Services", "Browsing the data in Services (Tables, Key-Values, Blobs, Messages, Message Queues, Time Series), and changing it (write)"),
            new TrakHoundAdminResource(Volumes, "Volumes", "Volume files, and creating, uploading and deleting them (write)"),
            new TrakHoundAdminResource(Logs, "Logs", "Log files and live Logs"),
            new TrakHoundAdminResource(Packages, "Packages", "Installed Packages and Package Sources' listings, and installing, updating and uninstalling Packages (write)"),
            new TrakHoundAdminResource(PackageSources, "Package Sources", "The Package Sources, and changing them (write)"),
            new TrakHoundAdminResource(AppConfigurations, "App Configurations", "App Configurations, and changing them (write)"),
            new TrakHoundAdminResource(ApiConfigurations, "Api Configurations", "Api Configurations, and changing them (write)"),
            new TrakHoundAdminResource(FunctionConfigurations, "Function Configurations", "Function Configurations, and changing them (write)"),
            new TrakHoundAdminResource(WorkerConfigurations, "Worker Configurations", "Worker Configurations, and changing them (write)"),
            new TrakHoundAdminResource(ServiceConfigurations, "Service Configurations", "Service Configurations, and changing them (write)"),
            new TrakHoundAdminResource(RouterConfigurations, "Routing", "Service Routers, and changing them (write)"),
            new TrakHoundAdminResource(GroupConfigurations, "Groups", "Module Groups, and changing them (write)"),
            new TrakHoundAdminResource(Variables, "Variables", "Configuration Variables, and changing them (write)"),
            new TrakHoundAdminResource(Settings, "Settings", "The Instance Settings, and changing them (write)"),
            new TrakHoundAdminResource(Deployments, "Deployments", "Deployments, Backups and Workspaces, and creating, installing and restoring them (write)"),
            new TrakHoundAdminResource(IdentityProviders, "Identity Providers", "Identity Provider Configurations, and changing them and installing Identity Packages (write)"),
            new TrakHoundAdminResource(SecurityProfiles, "Security Profiles", "Security Profiles, What is Public? and Access Check, and changing Profiles for Apis and Apps (write)"),
            new TrakHoundAdminResource(InstanceSecurityProfiles, "Instance Security Profiles", "Changing Profiles that reach the System API or Services, and giving Users and API Keys the Roles those Profiles use (write)"),
            new TrakHoundAdminResource(Users, "Users", "Users of the Users Identity Provider, and creating and changing them (write)"),
            new TrakHoundAdminResource(ApiKeys, "API Keys", "API Keys, and creating and revoking them (write)"),
            new TrakHoundAdminResource(TrustedPublishers, "Trusted Publishers", "The Publishers whose Packages are trusted, and changing them (write)"),
            new TrakHoundAdminResource(Zones, "Zones", "Zones and the Zone Map, and changing them (write)"),
            new TrakHoundAdminResource(Audit, "Audit Log", "The record of changes to Packages and Configurations"),
            new TrakHoundAdminResource(Licenses, "Licenses", "Licenses, and adding, assigning and removing them (write)"),
            new TrakHoundAdminResource(Admins, "Admin Accounts", "The Admin Accounts and their Roles, and changing them (write)")
        };

        public static readonly TrakHoundAdminRole Owner = new TrakHoundAdminRole
        {
            Id = OwnerRoleId,
            Name = "Owner",
            Description = "Everything, including the Admin Accounts",
            Grants = new List<TrakHoundAdminGrant> { new TrakHoundAdminGrant(TrakHoundZoneResources.Wildcard, WritePermission) }
        };

        public static readonly IReadOnlyList<TrakHoundAdminRole> Roles = new[]
        {
            Owner,
            new TrakHoundAdminRole
            {
                Id = PackagesRoleId,
                Name = "Packages",
                Description = "Installs Packages and configures Apps, Apis, Functions and Workers. No Services, Routing, Settings, Deployments or Security",
                Grants = new List<TrakHoundAdminGrant>
                {
                    new TrakHoundAdminGrant(Packages, WritePermission),
                    new TrakHoundAdminGrant(AppConfigurations, WritePermission),
                    new TrakHoundAdminGrant(ApiConfigurations, WritePermission),
                    new TrakHoundAdminGrant(FunctionConfigurations, WritePermission),
                    new TrakHoundAdminGrant(WorkerConfigurations, WritePermission),
                    new TrakHoundAdminGrant(Modules, WritePermission),
                    new TrakHoundAdminGrant(Volumes, WritePermission),
                    new TrakHoundAdminGrant(Instance, ReadPermission),
                    new TrakHoundAdminGrant(Logs, ReadPermission),
                    new TrakHoundAdminGrant(PackageSources, ReadPermission),
                    new TrakHoundAdminGrant(GroupConfigurations, ReadPermission)
                }
            },
            new TrakHoundAdminRole
            {
                Id = SecurityOperatorRoleId,
                Name = "Security Operator",
                Description = "Manages Users and API Keys. Views Security Profiles, Identity Providers and the Audit Log, but can't change them",
                Grants = new List<TrakHoundAdminGrant>
                {
                    new TrakHoundAdminGrant(Users, WritePermission),
                    new TrakHoundAdminGrant(ApiKeys, WritePermission),
                    new TrakHoundAdminGrant(SecurityProfiles, ReadPermission),
                    new TrakHoundAdminGrant(IdentityProviders, ReadPermission),
                    new TrakHoundAdminGrant(Audit, ReadPermission)
                }
            },
            new TrakHoundAdminRole
            {
                Id = ViewerRoleId,
                Name = "Viewer",
                Description = "Views everything except the Admin Accounts. Can't change anything",
                Grants = Resources.Where(o => o.Id != Admins).Select(o => new TrakHoundAdminGrant(o.Id, ReadPermission)).ToList()
            }
        };


        public static TrakHoundAdminResource GetResource(string resourceId)
        {
            return Resources.FirstOrDefault(o => o.Id == resourceId);
        }

        // The Roles an Owner added (see TrakHoundAdminRoleStore). Replaced, never modified, so they can be read without a lock
        private static volatile TrakHoundAdminRole[] _customRoles = Array.Empty<TrakHoundAdminRole>();

        static TrakHoundAdminSecurity()
        {
            foreach (var role in Roles) role.BuiltIn = true;
        }

        /// <summary>
        /// The Roles an Owner added to the built in Roles
        /// </summary>
        public static IReadOnlyList<TrakHoundAdminRole> CustomRoles => _customRoles;

        /// <summary>
        /// The built in Roles followed by the Roles an Owner added
        /// </summary>
        public static IReadOnlyList<TrakHoundAdminRole> AllRoles => Roles.Concat(_customRoles).ToList();

        /// <summary>
        /// Set by the TrakHoundAdminRoleStore whenever its Roles change
        /// </summary>
        public static void SetCustomRoles(IEnumerable<TrakHoundAdminRole> roles)
        {
            _customRoles = roles?.Where(o => o != null).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<TrakHoundAdminRole>();
        }

        public static TrakHoundAdminRole GetRole(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return null;

            return Roles.FirstOrDefault(o => string.Equals(o.Id, roleId, StringComparison.OrdinalIgnoreCase))
                ?? _customRoles.FirstOrDefault(o => string.Equals(o.Id, roleId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The Admin Interface page an Admin with the Roles starts on after signing in : the first area they can view
        /// </summary>
        public static string GetHomePath(IEnumerable<TrakHoundAdminRole> roles)
        {
            return GetHomePath(resourceId => IsAllowed(roles, resourceId, ReadPermission));
        }

        /// <summary>
        /// The Admin Interface page an Admin starts on after signing in : the first area they can view
        /// </summary>
        public static string GetHomePath(Func<string, bool> canView)
        {
            bool CanView(params string[] resourceIds) => resourceIds.Any(canView);

            if (CanView(ExplorerResources)) return "_admin/explorer";
            if (CanView(Packages)) return "_admin/packages";
            // Security before Configuration : an Admin who manages Users also views the Profiles and Providers that are under Configuration
            if (CanView(Users, ApiKeys, TrustedPublishers, Audit, Admins)) return "_admin/security";
            if (CanView(ConfigurationResources)) return "_admin/configuration";
            if (CanView(Deployments)) return "_admin/deployments";
            if (CanView(SecurityResources)) return "_admin/security";
            if (CanView(Licenses)) return "_admin/licenses";
            return "_admin/explorer";
        }

        /// <summary>
        /// Gets the Permission required for an action
        /// </summary>
        public static string GetPermission(bool write) => write ? WritePermission : ReadPermission;

        /// <summary>
        /// Gets the Admin Resource for the Configurations of a Category (ex. "service" => "admin:configuration:service").
        /// Identity Provider Configurations and Zones have their own Resources
        /// </summary>
        public static string GetConfigurationResource(string configurationCategory)
        {
            if (string.IsNullOrEmpty(configurationCategory)) return null;

            switch (configurationCategory.ToLowerInvariant())
            {
                case "identity": return IdentityProviders;
                case "zone": return Zones;
                default: return ConfigurationPrefix + configurationCategory.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Gets the Admin Resource an Admin needs "write" on (as well as Packages) to install a Package of a Category, since an installed
        /// Package gets a default Configuration
        /// </summary>
        public static string GetPackageResource(string packageCategory)
        {
            return GetConfigurationResource(packageCategory);
        }

        // What Profiles protect that manages the Instance itself (the System API and the MCP Server) or reaches Services directly
        private static readonly string[] _instanceResourcePrefixes = new[] { TrakHoundSystemSecurity.ResourcePrefix + ":", TrakHoundZoneResources.ServicePrefix + ":" };

        /// <summary>
        /// Whether a Security Profile Assignment's Resource (an ID, or a pattern ending with "*") can match a System Resource or a Service
        /// operation. Changing such an Assignment, or giving a User or API Key a Role it applies to, hands out access to the Instance
        /// itself, so it needs "write" on InstanceSecurityProfiles as well
        /// </summary>
        public static bool IsInstanceResource(string resourcePattern)
        {
            if (string.IsNullOrEmpty(resourcePattern)) return false;

            var pattern = TrakHoundSystemSecurity.MigrateResourceId(resourcePattern);
            if (pattern == TrakHoundZoneResources.Wildcard) return true;

            foreach (var prefix in _instanceResourcePrefixes)
            {
                if (pattern.EndsWith(TrakHoundZoneResources.Wildcard))
                {
                    // ex. "s*" and "system:logs*" both match System Resources
                    var start = pattern.TrimEnd('*');
                    if (prefix.StartsWith(start, StringComparison.OrdinalIgnoreCase) || start.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (pattern.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the Admin Resource (and Permission) for something a Client checks against Zones (see TrakHoundZoneResources), so the Admin
        /// Interface's own calls through a Client are checked in the same place. Null when there is nothing to check
        /// </summary>
        public static string GetZoneResource(string zoneResourceId, string zonePermission, out string permission)
        {
            permission = zonePermission == TrakHoundZoneResources.ReadPermission ? ReadPermission : WritePermission;
            if (string.IsNullOrEmpty(zoneResourceId)) return null;

            var parts = zoneResourceId.Split(':');
            switch (parts[0])
            {
                case TrakHoundZoneResources.VolumePrefix: return Volumes;
                case TrakHoundZoneResources.ServicePrefix: return Services;

                // Running a Function, and starting or stopping a Worker
                case TrakHoundZoneResources.FunctionPrefix:
                case TrakHoundZoneResources.WorkerPrefix:
                    permission = WritePermission;
                    return Modules;

                case TrakHoundZoneResources.ManagePrefix:
                    var category = parts.Length > 1 ? parts[1] : null;
                    if (category == "instance") return Instance;
                    if (category == "configuration") return parts.Length > 2 ? GetConfigurationResource(parts[2]) : null;
                    return Modules;
            }

            return null;
        }

        /// <summary>
        /// Gets what a call a Client checks against Zones is made on, for an Admin whose Role is limited to Zones : the Module being
        /// called, the Module a Configuration is for (ex. "manage:configuration:worker:reader"), or the Zone Resource itself
        /// </summary>
        public static TrakHoundAdminTarget GetZoneTarget(string targetMemberId, string zoneResourceId, string zonePermission)
        {
            if (targetMemberId == null && zoneResourceId != null)
            {
                var parts = zoneResourceId.Split(':');
                if (parts.Length > 3 && parts[0] == TrakHoundZoneResources.ManagePrefix && parts[1] == "configuration")
                {
                    return TrakHoundAdminTarget.Module(parts[2], string.Join(':', parts.Skip(3)));
                }
            }

            return TrakHoundAdminTarget.Call(targetMemberId, zoneResourceId, zonePermission);
        }

        /// <summary>
        /// Whether any of the Roles allows the Permission on the Resource
        /// </summary>
        public static bool IsAllowed(IEnumerable<TrakHoundAdminRole> roles, string resourceId, string permission)
        {
            return roles != null && roles.Any(o => o != null && o.Allows(resourceId, permission));
        }
    }

    /// <summary>
    /// Authorizes the Service operations made by the Admin Interface itself (ex. the Service explorers) against the signed in Admin's Roles.
    /// Used as the Security Context's Authorizer for those calls (see TrakHoundInstanceClient.EnterAdminContext)
    /// </summary>
    public sealed class TrakHoundAdminServiceAuthorizer : TrakHound.Security.ITrakHoundAuthorizer
    {
        private readonly TrakHoundAdminContext _context;


        public TrakHoundAdminServiceAuthorizer(TrakHoundAdminContext context)
        {
            _context = context;
        }


        public TrakHound.Security.TrakHoundAuthorizationResult Authorize(TrakHound.Security.TrakHoundIdentity identity, string resourceId, string permission = null)
        {
            var adminResource = TrakHoundAdminSecurity.GetZoneResource(resourceId, permission, out var adminPermission);
            if (adminResource != TrakHoundAdminSecurity.Services || _context == null) return TrakHound.Security.TrakHoundAuthorizationResult.Unrestricted();

            // For an Admin whose Role is limited to Zones : the operations a Rule of one of those Zones allows
            var zonePermission = adminPermission == TrakHoundAdminSecurity.WritePermission ? TrakHoundZoneResources.WritePermission : TrakHoundZoneResources.ReadPermission;
            if (_context.Can(adminResource, adminPermission, TrakHoundAdminTarget.Call(null, resourceId, zonePermission))) return TrakHound.Security.TrakHoundAuthorizationResult.Unrestricted();

            return TrakHound.Security.TrakHoundAuthorizationResult.Forbidden(TrakHoundAdminSecurity.DeniedMessage);
        }
    }

    /// <summary>
    /// The Admin Resource an Admin Interface Page belongs to. Admins without "read" on it get the No Access page instead (see InstanceRoutes).
    /// Several Resources can be listed when any one of them is enough (ex. the Explorer's home page)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class AdminResourceAttribute : Attribute
    {
        public string[] ResourceIds { get; }


        public AdminResourceAttribute(params string[] resourceIds)
        {
            ResourceIds = resourceIds;
        }
    }

    /// <summary>
    /// The Admin Interface Page checks its Admin Resource itself, since it depends on the Route (ex. the Configuration Category)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class AdminResourceDynamicAttribute : Attribute
    {
    }

    /// <summary>
    /// The Admin Interface Page is available to anyone who can reach the Admin Interface (ex. the Login page)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class AdminResourcePublicAttribute : Attribute
    {
    }
}
