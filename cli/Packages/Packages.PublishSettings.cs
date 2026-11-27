// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    internal static partial class Packages
    {
        /// <summary>
        /// The Publish Target a Package is published to, and the file it was read from
        /// </summary>
        internal class PublishSettings
        {
            /// <summary>
            /// The trakhound.package.publish.json file the Target was read from (null if no file was found, so --server and --organization are used)
            /// </summary>
            public string Path { get; set; }

            public string TargetName { get; set; }

            public TrakHoundPackagePublishTarget Target { get; set; }

            /// <summary>
            /// The Target's Destinations (null if no file was found)
            /// </summary>
            public List<TrakHoundPackagePublishDestination> Destinations { get; set; }

            public bool HasTarget => Target != null;
        }


        private static readonly string[] _publishSettingsProperties = { "defaultTarget", "targets" };
        private static readonly string[] _publishTargetProperties = { "destinations", "versionSuffix", "allowDirty", "requireSigning" };
        private static readonly string[] _publishDestinationProperties = { "packageServer", "organization", "apiTokenVariable" };


        /// <summary>
        /// Find the nearest Publish Settings file (trakhound.package.publish.json) to the Package directory and resolve the Target
        /// (the specified one, or the file's defaultTarget). Returns Publish Settings without a Target if no file is found,
        /// and null (after writing an error) if the file is invalid or the Target can't be resolved
        /// </summary>
        internal static PublishSettings ReadPublishSettings(string directory, string target = null, bool log = true)
        {
            var path = FindPublishSettings(directory);
            if (path == null)
            {
                if (!string.IsNullOrEmpty(target))
                {
                    Console.Error.WriteLine($"Error : Publish Target ({target}) not Found : No {TrakHoundPackagePublishSettings.Filename} in ({directory}) or its parent directories");
                    return null;
                }

                return new PublishSettings();
            }

            TrakHoundPackagePublishSettings settings;
            try
            {
                var json = File.ReadAllText(path);
                if (!ValidatePublishSettings(json, path)) return null;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                settings = JsonSerializer.Deserialize<TrakHoundPackagePublishSettings>(json, options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : Invalid Package Publish Settings ({path}) : {ex.Message}");
                return null;
            }

            var targets = new Dictionary<string, TrakHoundPackagePublishTarget>(settings?.Targets ?? new Dictionary<string, TrakHoundPackagePublishTarget>(), StringComparer.OrdinalIgnoreCase);
            if (targets.Count == 0)
            {
                Console.Error.WriteLine($"Error : No Publish Targets in ({path}) : Add one (ex. \"targets\": {{ \"development\": {{ \"destinations\": [ ... ] }} }})");
                return null;
            }

            // Every Target is checked, so a mistake in one that isn't used now is found before it is
            foreach (var entry in targets)
            {
                if (!ValidatePublishTarget(entry.Key, entry.Value, path)) return null;
            }

            var targetName = !string.IsNullOrEmpty(target) ? target : settings.DefaultTarget;
            if (string.IsNullOrEmpty(targetName))
            {
                Console.Error.WriteLine($"Error : No Publish Target : Set --target or \"defaultTarget\" in ({path}) : Targets = {string.Join(", ", targets.Keys)}");
                return null;
            }

            if (!targets.TryGetValue(targetName, out var publishTarget))
            {
                Console.Error.WriteLine($"Error : Publish Target ({targetName}) not Found in ({path}) : Targets = {string.Join(", ", targets.Keys)}");
                return null;
            }

            // The Target's name as written in the file
            targetName = targets.Keys.First(o => string.Equals(o, targetName, StringComparison.OrdinalIgnoreCase));

            if (log)
            {
                Console.WriteLine($"Package Publish Target : {targetName} : ({path})");

                // Only the nearest file is used (ex. a project created from a template has its own, with the 'local' Target), so say when it hides one in a parent directory
                var hiddenPath = GetHiddenPublishSettings(path);
                if (hiddenPath != null) Console.WriteLine($"Note : ({hiddenPath}) in a parent directory isn't used, since ({path}) is nearer. Delete the nearer one to use it");
            }

            return new PublishSettings
            {
                Path = path,
                TargetName = targetName,
                Target = publishTarget,
                Destinations = GetPublishDestinations(publishTarget.Destinations)
            };
        }

        /// <summary>
        /// The Publish Settings file in a parent directory of this one's directory, which this one hides (null if there isn't one)
        /// </summary>
        internal static string GetHiddenPublishSettings(string path)
        {
            var parent = Directory.GetParent(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            return parent != null ? FindPublishSettings(parent.FullName) : null;
        }

        /// <summary>
        /// Find the nearest Publish Settings file, starting in the Package directory and continuing through its parent directories
        /// </summary>
        private static string FindPublishSettings(string directory)
        {
            var current = !string.IsNullOrEmpty(directory) ? new DirectoryInfo(Path.GetFullPath(directory)) : null;
            while (current != null)
            {
                var path = Path.Combine(current.FullName, TrakHoundPackagePublishSettings.Filename);
                if (File.Exists(path)) return path;

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// Check the file's properties, so a setting in the wrong place (ex. a "version" or "destinations" left from the old format) or a misspelled one
        /// is reported instead of ignored
        /// </summary>
        private static bool ValidatePublishSettings(string json, string path)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine($"Error : Invalid Package Publish Settings ({path}) : Expected an object with \"defaultTarget\" and \"targets\"");
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (_publishSettingsProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;

                if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Error : \"version\" isn't used in ({path}) : Put a two part version prefix in the Package's {TrakHoundPackage.PackageConfigurationFilename} instead (ex. \"version\": \"0.3\"). The published version adds the git height (ex. 0.3.4)");
                }
                else if (string.Equals(property.Name, "destinations", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Error : \"destinations\" isn't used at the top of ({path}) : Move them into a Target (ex. \"defaultTarget\": \"development\", \"targets\": {{ \"development\": {{ \"destinations\": [ ... ] }} }})");
                }
                else
                {
                    Console.Error.WriteLine($"Error : Unknown property \"{property.Name}\" in ({path}) : Expected {string.Join(", ", _publishSettingsProperties)}");
                }

                return false;
            }

            if (document.RootElement.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Object)
            {
                foreach (var target in targets.EnumerateObject())
                {
                    if (target.Value.ValueKind != JsonValueKind.Object) continue;

                    foreach (var property in target.Value.EnumerateObject())
                    {
                        if (!_publishTargetProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine($"Error : Unknown property \"{property.Name}\" in Publish Target ({target.Name}) : ({path}) : Expected {string.Join(", ", _publishTargetProperties)}");
                            return false;
                        }
                    }

                    if (target.Value.TryGetProperty("destinations", out var destinations) && destinations.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var destination in destinations.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object))
                        {
                            foreach (var property in destination.EnumerateObject())
                            {
                                if (string.Equals(property.Name, "managementServer", StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.Error.WriteLine($"Error : \"managementServer\" in a Destination of Publish Target ({target.Name}) is now \"packageServer\" : ({path})");
                                    return false;
                                }

                                if (!_publishDestinationProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                                {
                                    Console.Error.WriteLine($"Error : Unknown property \"{property.Name}\" in a Destination of Publish Target ({target.Name}) : ({path}) : Expected {string.Join(", ", _publishDestinationProperties)}");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            return true;
        }

        private static bool ValidatePublishTarget(string name, TrakHoundPackagePublishTarget target, string path)
        {
            if (target == null || GetPublishDestinations(target.Destinations) == null)
            {
                Console.Error.WriteLine($"Error : Publish Target ({name}) has no Destinations : ({path})");
                return false;
            }

            if (!string.IsNullOrEmpty(target.VersionSuffix) && !IsValidVersionSuffix(target.VersionSuffix, $"\"versionSuffix\" of Publish Target ({name}) in ({path})"))
            {
                return false;
            }

            // A Package built with uncommitted changes must not get the Version of the commit, so each build needs its own Version
            if (target.AllowDirty && (string.IsNullOrEmpty(target.VersionSuffix) || target.VersionSuffix.IndexOf(TrakHoundPackagePublishTarget.TimestampPlaceholder, StringComparison.OrdinalIgnoreCase) < 0))
            {
                Console.Error.WriteLine($"Error : Publish Target ({name}) has \"allowDirty\" without {TrakHoundPackagePublishTarget.TimestampPlaceholder} in its \"versionSuffix\" : ({path}) : A build with uncommitted changes needs its own Version (ex. \"versionSuffix\": \"dev{TrakHoundPackagePublishTarget.BranchPlaceholder}.{TrakHoundPackagePublishTarget.TimestampPlaceholder}\")");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get the Destinations that have a Package Server or Organization set (null if there are none).
        /// Destinations left blank (ex. a template created without --packageServer) are ignored
        /// </summary>
        private static List<TrakHoundPackagePublishDestination> GetPublishDestinations(IEnumerable<TrakHoundPackagePublishDestination> destinations)
        {
            var result = destinations?
                .Where(o => o != null && (!string.IsNullOrWhiteSpace(o.PackageServer) || !string.IsNullOrWhiteSpace(o.Organization)))
                .ToList();

            return !result.IsNullOrEmpty() ? result : null;
        }

        /// <summary>
        /// With a Publish Target, --server and --organization replace its Destinations with one (ex. a development container on another port),
        /// and its other settings (versionSuffix, allowDirty, requireSigning) still apply. Either one can be left out when the Target has one
        /// Destination, which gives the other. The Target's apiTokenVariable isn't used for another Package Server, since its token is for the
        /// Target's own server : the CLI Configuration's Api Token (TRAKHOUND_API_TOKEN) is used instead.
        /// Returns false (after writing an error) when the Target has more than one Destination and only one of the two is given
        /// </summary>
        internal static bool ApplyServerOptions(PublishSettings publishSettings, string server, string organization, bool log = true)
        {
            if (publishSettings?.HasTarget != true || (string.IsNullOrEmpty(server) && string.IsNullOrEmpty(organization))) return true;

            var destinations = publishSettings.Destinations ?? new List<TrakHoundPackagePublishDestination>();
            var single = destinations.Count == 1 ? destinations[0] : null;

            if (single == null && (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(organization)))
            {
                Console.Error.WriteLine($"Error : Publish Target ({publishSettings.TargetName}) has {destinations.Count} Destinations, so replacing them needs both --server and --organization : ({publishSettings.Path})");
                return false;
            }

            var packageServer = !string.IsNullOrEmpty(server) ? server : single.PackageServer;
            var sameServer = single != null && string.Equals(packageServer?.TrimEnd('/'), single.PackageServer?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

            var destination = new TrakHoundPackagePublishDestination
            {
                PackageServer = packageServer,
                Organization = !string.IsNullOrEmpty(organization) ? organization : single.Organization,
                ApiTokenVariable = sameServer ? single.ApiTokenVariable : null
            };

            publishSettings.Destinations = new List<TrakHoundPackagePublishDestination> { destination };

            if (log) Console.WriteLine($"Publish Destination : ({destination.PackageServer} | {destination.Organization}) from --server/--organization, instead of the Destinations of Publish Target ({publishSettings.TargetName})");
            return true;
        }

        /// <summary>
        /// Check that every Destination's apiTokenVariable is set, so a publish doesn't stop partway through its uploads (or after a long build)
        /// because a Destination has no Api Token
        /// </summary>
        internal static bool CheckApiTokens(PublishSettings publishSettings)
        {
            if (publishSettings?.Destinations == null) return true;

            var success = true;
            foreach (var destination in publishSettings.Destinations)
            {
                if (!string.IsNullOrWhiteSpace(destination.ApiTokenVariable) && PackageClient.GetApiToken(destination) == null)
                {
                    Console.Error.WriteLine($"Error : The Environment Variable {destination.ApiTokenVariable.Trim()} isn't set : It holds the Api Token for ({destination.PackageServer} | {destination.Organization}) in Publish Target ({publishSettings.TargetName}) : ({publishSettings.Path})");
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Check the Package against its Publish Target before it's built : a Target with "requireSigning" needs a Signing Key, and only a Target
        /// with "allowDirty" builds a working tree with uncommitted changes (its Version Suffix has a {timestamp}, so each build has its own Version).
        /// Any other Target publishes the Version of the commit, which mustn't hold different files. --allow-dirty still works for a Target without
        /// "requireSigning". allowDirty is set to true for a Target with "allowDirty", so the build doesn't refuse to sign it
        /// </summary>
        internal static bool CheckPublishTarget(PublishSettings publishSettings, string directory, bool hasSigner, ref bool allowDirty)
        {
            if (publishSettings?.HasTarget != true) return true;

            var target = publishSettings.Target;
            var name = publishSettings.TargetName;

            if (target.RequireSigning && !hasSigner)
            {
                Console.Error.WriteLine($"Error : Publish Target ({name}) requires signed Packages : Set --sign-key or the {SigningKeyVariable} environment variable : ({publishSettings.Path})");
                return false;
            }

            if (target.AllowDirty)
            {
                allowDirty = true;
                return true;
            }

            var (_, commit, dirty) = GitCommands.GetBranchAndCommit(directory);
            if (dirty == true)
            {
                if (target.RequireSigning)
                {
                    Console.Error.WriteLine($"Error : The working tree has uncommitted changes : Publish Target ({name}) publishes signed Packages with the Version of commit {commit}, so they must be built from that commit{(allowDirty ? " (--allow-dirty can't be used with \"requireSigning\")" : null)}. Commit the changes, or publish to a Target with \"allowDirty\"");
                    return false;
                }

                if (!allowDirty)
                {
                    Console.Error.WriteLine($"Error : The working tree has uncommitted changes : Publish Target ({name}) publishes the Version of commit {commit}. Commit the changes, publish to a Target with \"allowDirty\", or use --allow-dirty");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the Version of the Package: --version, or the Prefix in its Package Information File and the git Height, with the Version Suffix
        /// (--version-suffix, or the Publish Target's) added. Returns null (after writing an error) if there's no Version
        /// </summary>
        internal static string GetPackageVersion(string directory, string informationPath, string version, string versionSuffix, PublishSettings publishSettings, bool log = true)
        {
            string baseVersion;
            string branch;
            string description;

            if (!string.IsNullOrEmpty(version))
            {
                baseVersion = version;
                branch = PackageVersions.GetBranch(directory);
                description = "--version";
            }
            else
            {
                var gitVersion = PackageVersions.Get(informationPath);
                if (gitVersion == null) return null;

                baseVersion = gitVersion.Version;
                branch = gitVersion.Branch;
                description = gitVersion.ToString();
            }

            var suffix = !string.IsNullOrEmpty(versionSuffix) ? versionSuffix : publishSettings?.Target?.VersionSuffix;
            if (!string.IsNullOrEmpty(suffix))
            {
                suffix = PackageVersions.ExpandSuffix(suffix, branch);
                if (!IsValidVersionSuffix(suffix)) return null;
            }

            var packageVersion = AddVersionSuffix(baseVersion, suffix);

            var target = publishSettings?.HasTarget == true ? $", target {publishSettings.TargetName}" : null;
            if (log) Console.WriteLine($"Package Version : {packageVersion} ({description}{target})");

            return packageVersion;
        }
    }
}
