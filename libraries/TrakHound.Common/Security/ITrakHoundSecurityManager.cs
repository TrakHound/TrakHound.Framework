// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrakHound.Security
{
    /// <summary>
    /// Manages Identity Providers, Resources, Security Profiles, and Assignments, and Authorizes Identities to access Resources
    /// </summary>
    public interface ITrakHoundSecurityManager : ITrakHoundAuthorizer
    {
        /// <summary>
        /// Verifies credentials sent with a Request (ex. an API Key or Bearer Token) using the Identity Providers that support Tokens.
        /// Returns NoResult if the Request doesn't contain credentials for any Identity Provider
        /// </summary>
        Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context);


        /// <summary>
        /// Register a Resource that can be secured (replaces an existing Resource with the same Id)
        /// </summary>
        void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId);

        /// <summary>
        /// Register a Resource that can be secured, with a single Permission (replaces an existing Resource with the same Id)
        /// </summary>
        void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId, string permission);

        /// <summary>
        /// Register a Resource that can be secured, with the specified Permissions (replaces an existing Resource with the same Id)
        /// </summary>
        void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId, IEnumerable<string> permissions);

        /// <summary>
        /// Gets the Resource with the specified Id (null if not found)
        /// </summary>
        TrakHoundIdentityResource GetResource(string resourceId);

        /// <summary>
        /// Gets all of the registered Resources (Api Endpoints, App Pages, and System Resources)
        /// </summary>
        IEnumerable<TrakHoundIdentityResource> GetResources();

        /// <summary>
        /// Removes the Resource with the specified Id
        /// </summary>
        void RemoveResource(string resourceId);


        /// <summary>
        /// Add (or replace) a Security Profile that uses the specified Identity Provider
        /// </summary>
        void AddProfile(string profileId, string providerId, string description = null);

        /// <summary>
        /// Add (or replace) a Security Profile, including its Assignments
        /// </summary>
        void AddProfile(TrakHoundSecurityProfile profile);

        /// <summary>
        /// Gets all of the Security Profiles
        /// </summary>
        IEnumerable<TrakHoundSecurityProfile> GetProfiles();

        /// <summary>
        /// Gets the Security Profile with the specified Id (null if not found)
        /// </summary>
        TrakHoundSecurityProfile GetProfile(string profileId);

        /// <summary>
        /// Removes the Security Profile and deletes its Configuration file
        /// </summary>
        void RemoveProfile(string profileId);


        /// <summary>
        /// Add (or replace) an Assignment in the specified Security Profile
        /// </summary>
        void AddAssignment(string profileId, TrakHoundIdentityAssignment assignment);

        /// <summary>
        /// Removes the Assignment with the specified Id from its Security Profile
        /// </summary>
        void RemoveAssignment(string assignmentId);


        /// <summary>
        /// Gets all of the loaded Identity Providers (including the built in API Key Identity Provider)
        /// </summary>
        IEnumerable<ITrakHoundIdentityProvider> GetProviders();

        /// <summary>
        /// Gets the Identity Provider with the specified Id (null if not found)
        /// </summary>
        ITrakHoundIdentityProvider GetProvider(string providerId);

        /// <summary>
        /// Gets the directory that the Identity Provider's Package is installed in (null if it wasn't loaded from a Package)
        /// </summary>
        string GetProviderPackageDirectory(string providerId);
    }
}
