// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Packages;

namespace TrakHound.CLI.Trust
{
    [CommandGroup("trust", "List, Add and Remove the publishers whose Package signatures a Configuration Profile trusts")]
    internal static class Trust
    {
        [Command("list", "List the Trusted Publishers")]
        [CommandHelp(null, "th trust list --profile default")]
        public static bool List(
            [CommandOption("The root directory containing the Configuration Profiles. If not specified, the current working directory is used", "-p")]
            string path = null,
            [CommandOption("The Configuration Profile")]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var publishers = TrakHoundTrustedPublishers.Read(GetProfilePath(path, profile)).Publishers;

            if (publishers.Count == 0) Console.WriteLine("No Trusted Publishers");

            foreach (var publisher in publishers)
            {
                var prefixes = !publisher.PackageIdPrefixes.IsNullOrEmpty() ? string.Join(", ", publisher.PackageIdPrefixes) : "any Package ID";
                Console.WriteLine($"{publisher.KeyId} : {publisher.Name} : {prefixes}");
            }

            return true;
        }

        [Command("add", "Trust a publisher's public key")]
        [CommandHelp(null, "th trust add ./contoso.public.pem --name Contoso --prefix Contoso.")]
        public static bool Add(
            [CommandParameter("The path to the publisher's public key file")]
            string publicKey,
            [CommandOption("The name of the publisher")]
            string name,
            [CommandOption("The Package IDs the publisher can sign, by the start of the ID (comma separated). If not specified, the publisher can sign any Package ID")]
            string prefix = null,
            [CommandOption("The root directory containing the Configuration Profiles. If not specified, the current working directory is used", "-p")]
            string path = null,
            [CommandOption("The Configuration Profile")]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            if (string.IsNullOrWhiteSpace(name)) throw new CommandException("--name is required");

            var keyPath = CliPath.Resolve(publicKey);
            if (!File.Exists(keyPath))
            {
                Console.Error.WriteLine($"Error : Public Key not Found : ({keyPath})");
                return false;
            }

            var profilePath = GetProfilePath(path, profile);
            var publishers = TrakHoundTrustedPublishers.Read(profilePath);

            TrakHoundTrustedPublisher publisher;
            try
            {
                publisher = publishers.Add(name, File.ReadAllText(keyPath), prefix?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error : {ex.Message}");
                return false;
            }

            publishers.Save(profilePath);

            Console.WriteLine($"Trusted Publisher Added : {publisher.KeyId} : {publisher.Name}");
            return true;
        }

        [Command("remove", "Stop trusting a publisher's key")]
        [CommandHelp(null, "th trust remove 3f2a9c01d4e5b678")]
        public static bool Remove(
            [CommandParameter("The Key ID (see th trust list)")]
            string keyId,
            [CommandOption("The root directory containing the Configuration Profiles. If not specified, the current working directory is used", "-p")]
            string path = null,
            [CommandOption("The Configuration Profile")]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var profilePath = GetProfilePath(path, profile);
            var publishers = TrakHoundTrustedPublishers.Read(profilePath);

            if (publishers.Publishers.RemoveAll(o => string.Equals(o.KeyId, keyId, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                Console.Error.WriteLine($"Error : Trusted Publisher not Found : ({keyId})");
                return false;
            }

            publishers.Save(profilePath);

            Console.WriteLine($"Trusted Publisher Removed : {keyId}");
            return true;
        }


        internal static string GetProfilePath(string path, string profile)
        {
            return Path.Combine(CliPath.Resolve(path), TrakHoundConfigurations.Directory, !string.IsNullOrEmpty(profile) ? profile : TrakHoundConfigurationProfile.Default);
        }
    }
}
