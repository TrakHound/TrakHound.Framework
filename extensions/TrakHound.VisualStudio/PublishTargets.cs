// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The Publish Targets in a Publish Settings file (trakhound.package.publish.json)
    /// </summary>
    internal class PublishTargetsFile
    {
        public string Path { get; set; }

        /// <summary>
        /// The Target names, in the order they are listed
        /// </summary>
        public List<string> Targets { get; set; } = new();

        public string DefaultTarget { get; set; }
    }

    /// <summary>
    /// Finds and reads Publish Settings files, which list the Targets ('th packages ... --target') a Package can be published to
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
        /// Read the Targets from a Publish Settings file. Throws if the file isn't valid
        /// </summary>
        public static PublishTargetsFile Read(string path)
        {
            // The same options the CLI reads the file with
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(File.ReadAllText(path), options);

            var file = new PublishTargetsFile { Path = path };
            if (document.RootElement.ValueKind != JsonValueKind.Object) return file;

            // The CLI reads property names without regard to case
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "defaultTarget", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    file.DefaultTarget = property.Value.GetString();
                }
                else if (string.Equals(property.Name, "targets", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var target in property.Value.EnumerateObject())
                    {
                        if (!file.Targets.Contains(target.Name, StringComparer.OrdinalIgnoreCase)) file.Targets.Add(target.Name);
                    }
                }
            }

            return file;
        }
    }
}
