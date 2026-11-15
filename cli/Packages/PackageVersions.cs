// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using LibGit2Sharp;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    /// <summary>
    /// A Package Version made from git: the two part Prefix in trakhound.package.json (ex. "0.3") and the git Height (ex. 0.3.4)
    /// </summary>
    internal class PackageGitVersion
    {
        /// <summary>
        /// [Major].[Minor].[Height] (ex. 0.3.4), without a Suffix
        /// </summary>
        public string Version { get; init; }

        public string Prefix { get; init; }

        /// <summary>
        /// The number of commits (including the commit itself) since the Prefix last changed. 0 when the Prefix change isn't committed
        /// or the Package isn't in a git Repository
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// The commit at HEAD (null if the Package isn't in a git Repository)
        /// </summary>
        public string Commit { get; init; }

        /// <summary>
        /// The current Branch (null on a detached HEAD, or if the Package isn't in a git Repository)
        /// </summary>
        public string Branch { get; init; }

        public override string ToString()
        {
            var commit = !string.IsNullOrEmpty(Commit) ? $", commit {Commit.Substring(0, Math.Min(7, Commit.Length))}" : null;
            return $"prefix {Prefix}, height {Height}{commit}";
        }
    }

    /// <summary>
    /// Package Versions made from git, the same way the TrakHound Framework's own Versions are: a Prefix set by hand, the git Height,
    /// and a Suffix (Prerelease Label) chosen by the Publish Target
    /// </summary>
    internal static class PackageVersions
    {
        private static readonly string _timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        private static readonly JsonDocumentOptions _jsonOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };


        /// <summary>
        /// The UTC time this run started (yyyyMMddHHmmss). Every Package built in one run gets the same {timestamp}
        /// </summary>
        public static string Timestamp => _timestamp;


        /// <summary>
        /// Returns true if the value is a two part Version Prefix ([Major].[Minor], ex. "0.3")
        /// </summary>
        public static bool IsValidPrefix(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^[0-9]+\\.[0-9]+$");
        }

        /// <summary>
        /// Replace the {timestamp} and {branch} Placeholders in a Version Suffix (ex. "dev{branch}.{timestamp}" is "dev.20261114153000" on main
        /// and "dev-feature-opc-ua.20261114153000" on feature/opc-ua)
        /// </summary>
        public static string ExpandSuffix(string versionSuffix, string branch)
        {
            if (string.IsNullOrEmpty(versionSuffix)) return versionSuffix;

            var branchValue = IsDefaultBranch(branch) ? "" : "-" + Regex.Replace(branch, "[^0-9A-Za-z-]", "-");

            return versionSuffix
                .Replace(TrakHoundPackagePublishTarget.TimestampPlaceholder, _timestamp, StringComparison.OrdinalIgnoreCase)
                .Replace(TrakHoundPackagePublishTarget.BranchPlaceholder, branchValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The default branch (main or master) has no {branch} in its Version Suffix. Neither does a detached HEAD (ex. a CI checkout of a commit)
        /// </summary>
        private static bool IsDefaultBranch(string branch)
        {
            return string.IsNullOrEmpty(branch)
                || string.Equals(branch, "main", StringComparison.OrdinalIgnoreCase)
                || string.Equals(branch, "master", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get the current Branch of the Repository containing the path (null on a detached HEAD, or if the path isn't in a Repository)
        /// </summary>
        public static string GetBranch(string path)
        {
            try
            {
                var gitPath = Repository.Discover(path);
                if (gitPath == null) return null;

                using var repo = new Repository(gitPath);
                return !repo.Info.IsHeadDetached ? repo.Head.FriendlyName : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read the Version Prefix from a Package Information File (trakhound.package.json). Returns null if it has none
        /// </summary>
        public static string ReadPrefix(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var document = JsonDocument.Parse(json, _jsonOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    }
                }
            }
            catch
            {
                // Not valid JSON (the Package Information File is reported when it's read)
            }

            return null;
        }

        /// <summary>
        /// Get the Version of the Package from the Prefix in its Package Information File and the git Height.
        /// Returns null (and writes an error) if the Package has no valid Prefix or the Repository is a shallow clone
        /// </summary>
        public static PackageGitVersion Get(string informationPath)
        {
            var fullPath = Path.GetFullPath(informationPath);
            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Error : Package Information File not Found : ({fullPath})");
                return null;
            }

            var prefix = ReadPrefix(File.ReadAllText(fullPath));
            if (string.IsNullOrEmpty(prefix))
            {
                Console.Error.WriteLine($"Error : No Package Version : ({fullPath})");
                Console.Error.WriteLine($"  Add a two part version prefix to {TrakHoundPackage.PackageConfigurationFilename} (ex. \"version\": \"0.1\"), or set --version. The published version is the prefix and the git height (ex. 0.1.4)");
                return null;
            }

            if (!IsValidPrefix(prefix))
            {
                Console.Error.WriteLine($"Error : Invalid Package Version Prefix ({prefix}) : ({fullPath})");
                Console.Error.WriteLine($"  The \"version\" in {TrakHoundPackage.PackageConfigurationFilename} is a two part prefix ([major].[minor], ex. \"0.3\"). The git height is added to it (ex. 0.3.4)");
                return null;
            }

            string gitPath;
            try
            {
                gitPath = Repository.Discover(Path.GetDirectoryName(fullPath));
            }
            catch
            {
                gitPath = null;
            }

            if (gitPath == null)
            {
                Console.WriteLine($"Warning : ({fullPath}) isn't in a git repository, so its version has no height ({prefix}.0). Set --version to choose it");
                return new PackageGitVersion { Version = $"{prefix}.0", Prefix = prefix, Height = 0 };
            }

            try
            {
                using var repo = new Repository(gitPath);

                if (repo.Info.IsShallow)
                {
                    Console.Error.WriteLine($"Error : ({repo.Info.WorkingDirectory}) is a shallow clone, so the git height of ({fullPath}) can't be counted. Fetch the full history (ex. git fetch --unshallow), or set --version");
                    return null;
                }

                var branch = !repo.Info.IsHeadDetached ? repo.Head.FriendlyName : null;
                var head = repo.Head.Tip;
                if (head == null)
                {
                    // A Repository without commits
                    return new PackageGitVersion { Version = $"{prefix}.0", Prefix = prefix, Height = 0, Branch = branch };
                }

                var trackedPath = GetTrackedPath(repo, fullPath);
                var height = 0;

                // A Prefix change that isn't committed yet starts the new version at height 0 (as the Framework's Revision does)
                if (trackedPath != null && string.Equals(ReadPrefixAt(repo, head, trackedPath, new Dictionary<string, string>()), prefix, StringComparison.Ordinal))
                {
                    height = GetHeight(repo, head, trackedPath);
                }

                return new PackageGitVersion { Version = $"{prefix}.{height}", Prefix = prefix, Height = height, Commit = head.Sha, Branch = branch };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : Unable to read the git history of ({fullPath}) : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The number of commits reachable from HEAD (including the commit itself) since the last commit that changed the Prefix, like
        /// 'git rev-list --count [commit]..HEAD' + 1. Merge commits don't count as changing it (as 'git log -G' doesn't show them),
        /// so a Prefix changed on a branch is found on that branch
        /// </summary>
        private static int GetHeight(Repository repo, Commit head, string trackedPath)
        {
            var prefixes = new Dictionary<string, string>();

            Commit versionCommit = null;
            foreach (var commit in repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = head, SortBy = CommitSortStrategies.Time }))
            {
                var parents = commit.Parents.ToList();
                if (parents.Count > 1) continue;

                var prefix = ReadPrefixAt(repo, commit, trackedPath, prefixes);
                var parentPrefix = parents.Count == 1 ? ReadPrefixAt(repo, parents[0], trackedPath, prefixes) : null;

                if (!string.Equals(prefix, parentPrefix, StringComparison.Ordinal))
                {
                    versionCommit = commit;
                    break;
                }
            }

            if (versionCommit == null)
            {
                return repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = head }).Count();
            }

            return repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = head, ExcludeReachableFrom = versionCommit }).Count() + 1;
        }

        /// <summary>
        /// The Prefix in the file at the commit (null if the file or its Prefix doesn't exist there). Prefixes are cached by blob
        /// </summary>
        private static string ReadPrefixAt(Repository repo, Commit commit, string trackedPath, Dictionary<string, string> prefixes)
        {
            var entry = commit[trackedPath];
            if (entry?.TargetType != TreeEntryTargetType.Blob) return null;

            var sha = entry.Target.Sha;
            if (!prefixes.TryGetValue(sha, out var prefix))
            {
                prefix = ReadPrefix(((Blob)entry.Target).GetContentText());
                prefixes[sha] = prefix;
            }

            return prefix;
        }

        /// <summary>
        /// The file's path in the Repository (with '/' separators), in the case that git tracks it (ex. a path typed in a different case on Windows).
        /// Returns null if the file isn't in the Repository's working directory
        /// </summary>
        private static string GetTrackedPath(Repository repo, string fullPath)
        {
            var workingDirectory = repo.Info.WorkingDirectory;
            if (string.IsNullOrEmpty(workingDirectory)) return null;

            var relativePath = Path.GetRelativePath(workingDirectory, fullPath).Replace('\\', '/');
            if (relativePath.StartsWith("../") || Path.IsPathRooted(relativePath)) return null;

            var tracked = repo.Index.FirstOrDefault(o => string.Equals(o.Path, relativePath, StringComparison.OrdinalIgnoreCase));
            return tracked?.Path ?? relativePath;
        }
    }
}
