// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.CLI
{
    internal static class PackageClient
    {
        private static readonly Dictionary<string, TrakHoundPackageClient> _clients = new Dictionary<string, TrakHoundPackageClient>();


        /// <summary>
        /// Get a Package Client. Server and Organization fall back to the CLI Configuration and then the defaults.
        /// The Api Token falls back to the CLI Configuration's (TRAKHOUND_API_TOKEN or apiToken in cli.config.yaml)
        /// </summary>
        public static TrakHoundPackageClient Get(string server = null, string organization = null, string apiToken = null)
        {
            var configuration = TrakHoundCliConfiguration.Instance;

            var baseUrl = FirstValue(server, configuration.PackageServer, TrakHoundCliConfiguration.DefaultPackageServer);
            var targetOrganization = FirstValue(organization, configuration.Organization, TrakHoundCliConfiguration.DefaultOrganization);
            var token = FirstValue(apiToken, configuration.ApiToken);

            // The same Server and Organization can be used with different Tokens (ex. two Publish Targets), so each gets its own Client
            var key = $"{baseUrl}|{targetOrganization}|{token}";
            lock (_clients)
            {
                if (!_clients.TryGetValue(key, out var packageClient))
                {
                    packageClient = new TrakHoundPackageClient(baseUrl, targetOrganization);
                    if (!string.IsNullOrEmpty(token)) packageClient.SetToken(token);

                    _clients[key] = packageClient;
                }

                return packageClient;
            }
        }

        /// <summary>
        /// Get a Package Client for a Publish Destination, with the Api Token from its apiTokenVariable (or the CLI Configuration's if it has none)
        /// </summary>
        public static TrakHoundPackageClient Get(TrakHoundPackagePublishDestination destination)
        {
            return Get(destination?.PackageServer, destination?.Organization, GetApiToken(destination));
        }

        /// <summary>
        /// The Api Token for a Publish Destination : the value of its apiTokenVariable Environment Variable (null if it has none, or it isn't set)
        /// </summary>
        public static string GetApiToken(TrakHoundPackagePublishDestination destination)
        {
            if (string.IsNullOrWhiteSpace(destination?.ApiTokenVariable)) return null;

            var value = Environment.GetEnvironmentVariable(destination.ApiTokenVariable.Trim());
            return !string.IsNullOrEmpty(value) ? value : null;
        }

        private static string FirstValue(params string[] values) => values.FirstOrDefault(o => !string.IsNullOrEmpty(o));
    }
}
