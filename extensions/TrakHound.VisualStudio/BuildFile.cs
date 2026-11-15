// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// A Project listed in a Build File
    /// </summary>
    internal class BuildFileProject
    {
        /// <summary>
        /// The full path of the Project directory
        /// </summary>
        public string Directory { get; set; }

        public string Name => Path.GetFileName(Directory);

        /// <summary>
        /// The Build File publishes the Package (otherwise it only builds the Package file)
        /// </summary>
        public bool Publish { get; set; }
    }

    /// <summary>
    /// Finds and reads Build Files (trakhound.build.json), which list the Projects that 'th packages build' builds and publishes
    /// </summary>
    internal static class BuildFile
    {
        // TrakHoundBuildFile.Filename (cli\Packages\TrakHoundBuildFile.cs)
        public const string Filename = "trakhound.build.json";

        // How far below the directory to look (ex. [repository]/modules/trakhound.build.json)
        private const int SearchDepth = 3;

        private static readonly string[] _ignoredDirectories = ["bin", "obj", "node_modules", "packages"];


        /// <summary>
        /// Find the Build Files in the directory, its parent directories, and the directories below it
        /// </summary>
        public static List<string> Find(string directory)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return paths;

            AddBelow(paths, new DirectoryInfo(directory), SearchDepth);

            var parent = new DirectoryInfo(directory).Parent;
            while (parent != null)
            {
                var path = Path.Combine(parent.FullName, Filename);
                if (File.Exists(path)) paths.Add(path);

                parent = parent.Parent;
            }

            return paths;
        }

        private static void AddBelow(List<string> paths, DirectoryInfo directory, int depth)
        {
            try
            {
                var path = Path.Combine(directory.FullName, Filename);
                if (File.Exists(path)) paths.Add(path);

                if (depth <= 0) return;

                foreach (var child in directory.EnumerateDirectories())
                {
                    // Hidden directories (ex. .git, .vs) and build output
                    if (child.Name.StartsWith('.') || _ignoredDirectories.Contains(child.Name, StringComparer.OrdinalIgnoreCase)) continue;

                    AddBelow(paths, child, depth - 1);
                }
            }
            catch
            {
                // A directory that can't be read
            }
        }

        /// <summary>
        /// Read the Projects from a Build File, in the order they are listed.
        /// Throws if the file isn't valid
        /// </summary>
        public static List<BuildFileProject> ReadProjects(string buildFilePath)
        {
            // The same options the CLI reads the file with
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(File.ReadAllText(buildFilePath), options);

            var root = document.RootElement;
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(buildFilePath));

            var defaultPublish = TryGetProperty(root, "defaults", out var defaults) && GetBoolean(defaults, "publish") == true;

            var projects = new List<BuildFileProject>();
            if (TryGetProperty(root, "projects", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!TryGetProperty(item, "path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String) continue;

                    var path = pathElement.GetString();
                    if (string.IsNullOrEmpty(path)) continue;

                    // A Project's path is its directory or its .csproj file
                    var fullPath = Path.GetFullPath(path, baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) fullPath = Path.GetDirectoryName(fullPath);

                    projects.Add(new BuildFileProject
                    {
                        Directory = fullPath,
                        Publish = GetBoolean(item, "publish") ?? defaultPublish,
                    });
                }
            }

            return projects;
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

        private static bool? GetBoolean(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
    }
}
