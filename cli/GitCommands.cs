// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using LibGit2Sharp;

namespace TrakHound.CLI
{
    static class GitCommands
    {
        /// <summary>
        /// Get the current Branch, latest Commit SHA and whether the working tree has changes that aren't in that Commit (opens the Repository once).
        /// Untracked files count, since a project can compile them. Files the CLI writes itself don't, since they aren't build inputs and publishing
        /// would otherwise mark the next project in the same Repository as changed. Returns nulls if the path is not in a Repository
        /// </summary>
        public static (string Branch, string Commit, bool? Dirty) GetBranchAndCommit(string path)
        {
            try
            {
                var gitPath = Repository.Discover(path);
                if (gitPath != null)
                {
                    using (var repo = new Repository(gitPath))
                    {
                        var dirty = repo.RetrieveStatus().Any(o => o.State != FileStatus.Ignored && o.State != FileStatus.Unaltered && !IsWrittenByCli(o.FilePath));

                        return (repo.Head.FriendlyName, repo.Head.Tip?.Sha, dirty);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Git Error : GetBranchAndCommit() : {ex.Message}");
            }

            return (null, null, null);
        }

        /// <summary>
        /// Get the root of the Repository containing the path, in the same form as the path (git would expand a short path, and it has to match
        /// the paths the compiler sees). Returns the path itself if it is not in a Repository
        /// </summary>
        public static string GetRootDirectory(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
            }

            return path;
        }

        private static bool IsWrittenByCli(string path)
        {
            return path.EndsWith(TrakHound.Packages.TrakHoundPackage.FileExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the path is in a Repository
        /// </summary>
        public static bool IsInRepository(string path)
        {
            try
            {
                return Repository.Discover(path) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
