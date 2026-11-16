// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Token.Identity
{
    public class Module : TrakHoundIdentityProviderBase
    {
        private const string _userIdParameterKey = "userId";
        private const string _tokenParameterKey = "token";
        private const string _rolesParameterKey = "roles";


        public Module(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables) : base(configuration, client, volume, variables) { }


        public override TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Token;


        // The Token is sent in the "token" Header (or Query Parameter)
        protected override Task<TrakHoundIdentityResult> OnAuthenticate(TrakHoundIdentityContext context)
        {
            var requestToken = context.Request.GetHeader(_tokenParameterKey) ?? context.Request.GetQueryParameter(_tokenParameterKey);
            if (string.IsNullOrEmpty(requestToken)) return Task.FromResult(TrakHoundIdentityResult.NoResult());

            // Read Configuration
            var configurationToken = Configuration.GetParameter(_tokenParameterKey);

            if (IsTokenMatch(requestToken, configurationToken))
            {
                var identity = new TrakHoundIdentity();
                identity.ProviderId = Id;
                identity.UserId = GetUserId();
                identity.Type = TrakHoundIdentityUserType.Origin;
                identity.Roles = GetRoles();

                return Task.FromResult(TrakHoundIdentityResult.Success(identity));
            }

            return Task.FromResult(TrakHoundIdentityResult.Fail("Invalid Token"));
        }


        // Without a User ID, requests are signed in as the provider, so logs and the Audit Log still show who made them
        private string GetUserId()
        {
            var userId = Configuration.GetParameter(_userIdParameterKey);
            return !string.IsNullOrEmpty(userId) ? userId : Id;
        }

        // Roles are configured as a comma separated list (ex. "operators,engineers")
        private string[] GetRoles() => GetListParameter(_rolesParameterKey);

        private static bool IsTokenMatch(string requestToken, string configurationToken)
        {
            if (string.IsNullOrEmpty(requestToken) || string.IsNullOrEmpty(configurationToken)) return false;

            // Constant time comparison (prevents timing attacks)
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(requestToken), Encoding.UTF8.GetBytes(configurationToken));
        }
    }
}
