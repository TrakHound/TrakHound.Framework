// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// A Destination of a Publish Target : the Package Server and Organization it uploads to
    /// </summary>
    internal class PublishTargetDestination
    {
        public string ManagementServer { get; set; }

        public string Organization { get; set; }

        /// <summary>
        /// The Environment Variable that holds the Api Token for this Destination (the CLI uses TRAKHOUND_API_TOKEN when not set)
        /// </summary>
        public string ApiTokenVariable { get; set; }
    }

    /// <summary>
    /// A Publish Target ('th packages ... --target') in a Publish Settings file
    /// </summary>
    internal class PublishTarget
    {
        public string Name { get; set; }

        public List<PublishTargetDestination> Destinations { get; set; } = new();

        public string VersionSuffix { get; set; }

        public bool AllowDirty { get; set; }

        public bool RequireSigning { get; set; }
    }

    /// <summary>
    /// The Publish Targets in a Publish Settings file (trakhound.package.publish.json)
    /// </summary>
    internal class PublishTargetsFile
    {
        public string Path { get; set; }

        /// <summary>
        /// The Targets, in the order they are listed
        /// </summary>
        public List<PublishTarget> Targets { get; set; } = new();

        public string DefaultTarget { get; set; }

        /// <summary>
        /// The Publish Settings file in a parent directory that this one hides (the CLI only uses the nearest file)
        /// </summary>
        public string HiddenPath { get; set; }

        public PublishTarget GetTarget(string name) => Targets.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Targets that a set of Packages can be published to together
    /// </summary>
    internal class PublishTargetChoices
    {
        public static readonly PublishTargetChoices None = new();

        /// <summary>
        /// The Publish Settings files nearest to the Packages
        /// </summary>
        public List<PublishTargetsFile> Files { get; set; } = new();

        /// <summary>
        /// The Targets every file has, in the order the first file lists them. Each one's Destinations are those of every file
        /// </summary>
        public List<PublishTarget> Targets { get; set; } = new();

        /// <summary>
        /// The defaultTarget, when every file has the same one (and it is in Targets)
        /// </summary>
        public string DefaultTarget { get; set; }

        /// <summary>
        /// The file (or how many files) the Targets were read from
        /// </summary>
        public string Source => Files.Count == 1 ? Files[0].Path : $"{Files.Count} {PublishTargets.Filename} files";

        /// <summary>
        /// Shown when the files hide one in a parent directory (ex. a project created from a template, in a repository with a shared file)
        /// </summary>
        public string HiddenNote
        {
            get
            {
                var hidden = Files.Select(o => o.HiddenPath).Where(o => o != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (hidden.Count == 0) return null;

                return hidden.Count == 1
                    ? $"{hidden[0]} isn't used, since this one is nearer. Delete the nearer one to use it"
                    : $"{hidden.Count} {PublishTargets.Filename} files in parent directories aren't used, since these are nearer";
            }
        }
    }

    /// <summary>
    /// Finds and reads Publish Settings files, which list the Targets a Package can be published to
    /// </summary>
    internal static class PublishTargets
    {
        // TrakHoundPackagePublishSettings.Filename (libraries\TrakHound.Common\Packages\TrakHoundPackagePublishSettings.cs)
        public const string Filename = "trakhound.package.publish.json";


        /// <summary>
        /// Find the nearest Publish Settings file to a Package directory, the same way the CLI does (the directory, then its parent directories).
        /// Returns null if there isn't one
        /// </summary>
        public static string Find(string directory)
        {
            var current = !string.IsNullOrEmpty(directory) ? new DirectoryInfo(Path.GetFullPath(directory)) : null;
            while (current != null)
            {
                var path = Path.Combine(current.FullName, Filename);
                if (File.Exists(path)) return path;

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// The Targets that the Packages in these directories can all be published to, since the CLI publishes each Package to the Target
        /// in its own nearest file. None when there is no file, or a file can't be read (the CLI reports it when publishing)
        /// </summary>
        public static PublishTargetChoices GetChoices(IEnumerable<string> packageDirectories)
        {
            var files = new List<PublishTargetsFile>();
            foreach (var path in packageDirectories.Select(Find).Where(o => o != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    files.Add(Read(path));
                }
                catch
                {
                    return PublishTargetChoices.None;
                }
            }
            if (files.Count == 0) return PublishTargetChoices.None;

            var choices = new PublishTargetChoices { Files = files };
            foreach (var target in files[0].Targets)
            {
                var targets = files.Select(o => o.GetTarget(target.Name)).ToList();
                if (targets.Any(o => o == null)) continue;

                choices.Targets.Add(new PublishTarget
                {
                    Name = target.Name,
                    Destinations = targets.SelectMany(o => o.Destinations)
                        .GroupBy(o => $"{o.ManagementServer}|{o.Organization}|{o.ApiTokenVariable}", StringComparer.OrdinalIgnoreCase)
                        .Select(o => o.First())
                        .ToList(),
                    VersionSuffix = target.VersionSuffix,
                    AllowDirty = targets.All(o => o.AllowDirty),
                    RequireSigning = targets.Any(o => o.RequireSigning),
                });
            }

            var defaults = files.Select(o => o.DefaultTarget).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (defaults.Count == 1 && choices.Targets.Any(o => string.Equals(o.Name, defaults[0], StringComparison.OrdinalIgnoreCase)))
            {
                choices.DefaultTarget = defaults[0];
            }

            return choices;
        }

        /// <summary>
        /// Read the Targets from a Publish Settings file. Throws if the file isn't valid
        /// </summary>
        public static PublishTargetsFile Read(string path)
        {
            // The same options the CLI reads the file with
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(File.ReadAllText(path), options);

            var parent = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(path)));
            var file = new PublishTargetsFile { Path = path, HiddenPath = parent != null ? Find(parent.FullName) : null };
            if (document.RootElement.ValueKind != JsonValueKind.Object) return file;

            file.DefaultTarget = GetString(document.RootElement, "defaultTarget");

            if (TryGetProperty(document.RootElement, "targets", out var targets) && targets.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in targets.EnumerateObject())
                {
                    if (file.GetTarget(item.Name) != null || item.Value.ValueKind != JsonValueKind.Object) continue;

                    var target = new PublishTarget
                    {
                        Name = item.Name,
                        VersionSuffix = GetString(item.Value, "versionSuffix"),
                        AllowDirty = GetBoolean(item.Value, "allowDirty"),
                        RequireSigning = GetBoolean(item.Value, "requireSigning"),
                    };

                    if (TryGetProperty(item.Value, "destinations", out var destinations) && destinations.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var destination in destinations.EnumerateArray())
                        {
                            var managementServer = GetString(destination, "managementServer");
                            var organization = GetString(destination, "organization");

                            // The CLI ignores Destinations left blank (ex. a template created without --managementServer)
                            if (string.IsNullOrWhiteSpace(managementServer) && string.IsNullOrWhiteSpace(organization)) continue;

                            target.Destinations.Add(new PublishTargetDestination
                            {
                                ManagementServer = managementServer,
                                Organization = organization,
                                ApiTokenVariable = GetString(destination, "apiTokenVariable")?.Trim(),
                            });
                        }
                    }

                    file.Targets.Add(target);
                }
            }

            return file;
        }

        // The CLI reads property names without regard to case
        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string GetString(JsonElement element, string name)
        {
            return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool GetBoolean(JsonElement element, string name)
        {
            return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }
}
