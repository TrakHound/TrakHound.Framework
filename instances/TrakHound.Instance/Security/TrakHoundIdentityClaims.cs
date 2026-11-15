// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Security.Claims;
using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Converts between a TrakHound Identity and the ClaimsPrincipal stored in the Instance's Session Cookie
    /// </summary>
    public static class TrakHoundIdentityClaims
    {
        public const string AuthenticationScheme = "TrakHound.Identity";

        private const string _providerClaim = "th:provider";
        private const string _userIdClaim = "th:user";
        private const string _nameClaim = "th:name";
        private const string _typeClaim = "th:type";
        private const string _roleClaim = "th:role";
        private const string _expiresClaim = "th:expires";
        private const string _propertyClaimPrefix = "th:property:";


        public static ClaimsPrincipal ToPrincipal(TrakHoundIdentity identity)
        {
            var claims = new List<Claim>();
            claims.Add(new Claim(_providerClaim, identity.ProviderId ?? ""));
            claims.Add(new Claim(_userIdClaim, identity.UserId ?? ""));
            if (!string.IsNullOrEmpty(identity.Name)) claims.Add(new Claim(_nameClaim, identity.Name));
            claims.Add(new Claim(_typeClaim, identity.Type.ToString()));

            if (identity.Roles != null)
            {
                foreach (var role in identity.Roles.Where(o => !string.IsNullOrEmpty(o))) claims.Add(new Claim(_roleClaim, role));
            }

            if (identity.Properties != null)
            {
                foreach (var property in identity.Properties.Where(o => o.Value != null)) claims.Add(new Claim(_propertyClaimPrefix + property.Key, property.Value));
            }

            if (identity.Expires.HasValue)
            {
                claims.Add(new Claim(_expiresClaim, identity.Expires.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme, _nameClaim, _roleClaim));
        }

        /// <summary>
        /// Reads the Identity from a ClaimsPrincipal (null if the Principal is not a TrakHound Identity or it has expired)
        /// </summary>
        public static TrakHoundIdentity FromPrincipal(ClaimsPrincipal principal)
        {
            var claimsIdentity = principal?.Identities?.FirstOrDefault(o => o.AuthenticationType == AuthenticationScheme && o.IsAuthenticated);
            if (claimsIdentity != null)
            {
                var providerId = claimsIdentity.FindFirst(_providerClaim)?.Value;
                if (!string.IsNullOrEmpty(providerId))
                {
                    var identity = new TrakHoundIdentity();
                    identity.ProviderId = providerId;
                    identity.UserId = claimsIdentity.FindFirst(_userIdClaim)?.Value;
                    identity.Name = claimsIdentity.FindFirst(_nameClaim)?.Value;
                    identity.Type = claimsIdentity.FindFirst(_typeClaim)?.Value.ConvertEnum<TrakHoundIdentityUserType>() ?? TrakHoundIdentityUserType.Origin;
                    identity.Roles = claimsIdentity.FindAll(_roleClaim).Select(o => o.Value).ToList();
                    identity.Properties = claimsIdentity.Claims
                        .Where(o => o.Type.StartsWith(_propertyClaimPrefix))
                        .GroupBy(o => o.Type.Substring(_propertyClaimPrefix.Length))
                        .ToDictionary(o => o.Key, o => o.First().Value);

                    var expires = claimsIdentity.FindFirst(_expiresClaim)?.Value;
                    if (expires != null && DateTime.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresTime))
                    {
                        identity.Expires = expiresTime.ToUniversalTime();
                    }

                    if (!identity.IsExpired) return identity;
                }
            }

            return null;
        }
    }
}
