// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance
{
    /// <summary>
    /// Links to the TrakHound website (its docs, licensing and contact pages) used by the Admin interface. Only links : the Instance never requests anything from the website
    /// </summary>
    public static class TrakHoundWebsite
    {
        // The website's address : every link is made from it, so it's changed in one place
        public const string BaseUrl = "https://trakhound.com";

        public const string LicenseRequestUrl = "https://licenses.trakhound.com/request";
        public const string GitHubUrl = "https://github.com/TrakHound/TrakHound.Framework";


        public static string Home => BaseUrl + "/";

        public static string Contact => Link("contact");

        public static string FrameworkDocs => Link("docs/trakhound-framework");

        public static string LicensingGuide => Link("docs/licensing");


        /// <summary>
        /// The website page at path (ex. "contact")
        /// </summary>
        public static string Link(string path) => $"{BaseUrl}/{path?.TrimStart('/')}";

        /// <summary>
        /// A Framework docs page (a Topic, ex. Topics.Zones), with an optional section (ex. "variables")
        /// </summary>
        public static string Docs(string topic, string section = null)
        {
            var url = $"{FrameworkDocs}/{topic}";
            return !string.IsNullOrEmpty(section) ? $"{url}#{section}" : url;
        }


        /// <summary>
        /// The Framework docs pages the Admin interface links to (the page names on the website, so they must match it)
        /// </summary>
        public static class Topics
        {
            public const string GettingStarted = "getting-started";
            public const string Instances = "instances";
            public const string Packages = "packages";
            public const string Configuration = "configuration";
            public const string VolumesAndVariables = "volumes-and-variables";
            public const string Apis = "apis";
            public const string Apps = "apps";
            public const string Functions = "functions";
            public const string Workers = "workers";
            public const string Identity = "identity";
            public const string Services = "services";
            public const string Routing = "routing";
            public const string Security = "security";
            public const string Zones = "zones";
            public const string McpServer = "mcp-server";
            public const string ApiCatalog = "api-catalog";
            public const string SelfHostingPackages = "self-hosting-packages";
            public const string FirstPartyPackages = "first-party-packages";

            public static IReadOnlyList<string> All { get; } = new[]
            {
                GettingStarted, Instances, Packages, Configuration, VolumesAndVariables, Apis, Apps, Functions, Workers, Identity,
                Services, Routing, Security, Zones, McpServer, ApiCatalog, SelfHostingPackages, FirstPartyPackages
            };
        }


        /// <summary>
        /// The help page for an Admin interface page, from its path relative to the Instance (ex. "_admin/configuration/zones"). Null when it has none
        /// </summary>
        public static string GetAdminHelpLink(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var end = path.IndexOfAny(new[] { '?', '#' });
            if (end >= 0) path = path.Substring(0, end);

            var segments = path.Trim('/').ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (segments.Count < 1 || segments[0] != "_admin") return null;
            segments.RemoveAt(0);

            // The Admin interface's home is the Explorer
            if (segments.Count == 0) segments.Add("explorer");

            // A Workspace's Configuration has the same pages as the Instance's
            if (segments.Count > 2 && segments[0] == "workspaces") segments.RemoveRange(0, 2);

            string Segment(int i) => i < segments.Count ? segments[i] : null;

            switch (segments[0])
            {
                case "explorer":
                    // _admin/explorer/{instanceId}/{section}
                    switch (Segment(2))
                    {
                        case "api": return Docs(Topics.Apis);
                        case "apps": return Docs(Topics.Apps);
                        case "functions":
                        case "functions-board":
                        case "functions-summary": return Docs(Topics.Functions);
                        case "workers": return Docs(Topics.Workers);
                        case "services": return Docs(Topics.Services);
                        case "diagnostics":
                            switch (Segment(3))
                            {
                                case "mcp": return Docs(Topics.McpServer);
                                case "health": return Docs(Topics.Instances, "health-checks");
                                case "logs": return Docs(Topics.Instances, "logs");
                            }
                            return Docs(Topics.Instances);
                    }
                    return Docs(Topics.Instances);

                case "packages":
                    return Segment(1) == "sources" ? Docs(Topics.SelfHostingPackages) : Docs(Topics.Packages);

                case "configuration":
                    switch (Segment(1))
                    {
                        case "apps": return Docs(Topics.Apps);
                        case "api": return Docs(Topics.Apis);
                        case "functions": return Docs(Topics.Functions);
                        case "workers": return Docs(Topics.Workers);
                        case "services": return Docs(Topics.Services);
                        case "identity": return Docs(Topics.Identity);
                        case "variables": return Docs(Topics.VolumesAndVariables, "variables");
                        case "groups": return Docs(Topics.Configuration, "groups");
                        case "routing": return Docs(Topics.Routing);
                        case "security-profiles": return Docs(Topics.Security);
                        case "zones":
                        case "zone-map": return Docs(Topics.Zones);
                        case "settings": return Docs(Topics.Instances);
                    }
                    return Docs(Topics.Configuration);

                case "deployments":
                    return Docs(Topics.Configuration, "deployments");

                case "security":
                    switch (Segment(1))
                    {
                        case "trusted-publishers": return Docs(Topics.Packages, "verification");
                        case "audit": return Docs(Topics.Security, "audit");
                        case "public": return Docs(Topics.Security, "unprotected");
                    }
                    return Docs(Topics.Security);

                case "licenses":
                    return LicensingGuide;
            }

            return null;
        }
    }
}
