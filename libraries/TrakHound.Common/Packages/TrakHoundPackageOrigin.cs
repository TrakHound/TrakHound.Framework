// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// Where an installed Package was downloaded from : a Package Server and the Organization on it. Recorded by the Package Manager
    /// (in the packages file), not in the Package, since the same Package file can be published to several Organizations
    /// </summary>
    public class TrakHoundPackageOrigin
    {
        /// <summary>
        /// The base URL of the Package Server
        /// </summary>
        [JsonPropertyName("server")]
        public string Server { get; set; }

        /// <summary>
        /// The Organization on the Package Server
        /// </summary>
        [JsonPropertyName("organization")]
        public string Organization { get; set; }


        public TrakHoundPackageOrigin() { }

        public TrakHoundPackageOrigin(string server, string organization)
        {
            Server = server;
            Organization = organization;
        }


        /// <summary>
        /// Whether the Origin is the Package Server and Organization (ignoring case and a trailing '/', the way Organizations are matched)
        /// </summary>
        public bool Matches(string server, string organization)
        {
            return IsSameServer(Server, server) && string.Equals(Organization, organization, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether two base URLs are the same Package Server (ignoring case and a trailing '/')
        /// </summary>
        public static bool IsSameServer(string server, string otherServer)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(otherServer)) return false;
            return string.Equals(server.TrimEnd('/'), otherServer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => $"{Organization} ({Server})";
    }
}
