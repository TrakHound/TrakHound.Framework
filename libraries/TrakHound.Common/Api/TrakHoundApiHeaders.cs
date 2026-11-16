// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Api
{
    /// <summary>
    /// The Request Headers passed to Api Endpoints ([FromHeader] Parameters)
    /// </summary>
    public static class TrakHoundApiHeaders
    {
        // A HashSet for IsWithheld. Withheld is exposed as IReadOnlyCollection because IReadOnlySet isn't in .NET Standard 2.0 (TrakHound.Client)
        private static readonly HashSet<string> _withheld = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cookie",
            "Authorization",
            "Proxy-Authorization",
            "X-API-Key",
            "X-TrakHound-Module-Token",
            "token"
        };

        /// <summary>
        /// Headers that are never passed to an Api, because they carry the caller's credentials for the Instance
        /// (the Admin Session Cookie, Api Keys, Bearer Tokens, Module Tokens and the Token Identity Provider's Token).
        /// An Api that could read them could act as its caller
        /// </summary>
        public static readonly IReadOnlyCollection<string> Withheld = _withheld;


        /// <summary>
        /// Returns true if the Header is never passed to an Api (case-insensitive)
        /// </summary>
        public static bool IsWithheld(string name) => !string.IsNullOrEmpty(name) && _withheld.Contains(name);

        /// <summary>
        /// Copies the Headers that may be passed to an Api into a case-insensitive Dictionary (Withheld Headers are removed).
        /// Returns null if there are none
        /// </summary>
        public static Dictionary<string, string> Filter(IEnumerable<KeyValuePair<string, string>> headers)
        {
            if (headers == null) return null;

            Dictionary<string, string> filtered = null;
            foreach (var header in headers)
            {
                if (string.IsNullOrEmpty(header.Key) || IsWithheld(header.Key)) continue;

                filtered ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                filtered[header.Key] = header.Value;
            }

            return filtered;
        }
    }
}
