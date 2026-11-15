// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using LibGit2Sharp;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// A git Repository in a temporary directory, deleted when disposed
    /// </summary>
    internal sealed class TestRepository : IDisposable
    {
        private static readonly Signature _signature = new Signature("Test", "test@example.com", DateTimeOffset.UtcNow);
        private int _commits;

        public string Directory { get; }


        public TestRepository(bool init = true)
        {
            Directory = Path.Combine(Path.GetTempPath(), $"trakhound-cli-tests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);

            if (init) Repository.Init(Directory);
        }

        public string PathOf(string relativePath) => Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public string Write(string relativePath, string contents)
        {
            var path = PathOf(relativePath);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
            return path;
        }

        /// <summary>
        /// Write a Package Information File (trakhound.package.json) with the Version Prefix (none if null)
        /// </summary>
        public string WritePackage(string relativeDirectory, string prefix)
        {
            var version = prefix != null ? $",\n  \"version\": \"{prefix}\"" : null;
            return Write($"{relativeDirectory}/trakhound.package.json", $"{{\n  \"id\": \"Test.Package\",\n  \"category\": \"api\"{version}\n}}\n");
        }

        /// <summary>
        /// Commit everything in the working tree (an empty commit if nothing changed). Returns the Commit
        /// </summary>
        public Commit Commit(string message = null)
        {
            using var repo = new Repository(Directory);
            Commands.Stage(repo, "*");

            var signature = new Signature(_signature.Name, _signature.Email, _signature.When.AddSeconds(++_commits));
            return repo.Commit(message ?? $"Commit {_commits}", signature, signature, new CommitOptions { AllowEmptyCommit = true });
        }

        /// <summary>
        /// Commit a change to an unrelated file (anywhere in the Repository)
        /// </summary>
        public Commit CommitOther(string relativePath = "other.txt")
        {
            Write(relativePath, Guid.NewGuid().ToString());
            return Commit();
        }

        /// <summary>
        /// The current Branch (the default one depends on git's init.defaultBranch, ex. master or main)
        /// </summary>
        public string CurrentBranch
        {
            get
            {
                using var repo = new Repository(Directory);
                return repo.Head.FriendlyName;
            }
        }

        public void Checkout(string branch, bool create = false)
        {
            using var repo = new Repository(Directory);
            var target = create ? repo.CreateBranch(branch) : repo.Branches[branch];
            Commands.Checkout(repo, target);
        }

        public void Merge(string branch)
        {
            using var repo = new Repository(Directory);
            var signature = new Signature(_signature.Name, _signature.Email, _signature.When.AddSeconds(++_commits));
            repo.Merge(repo.Branches[branch], signature, new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });
        }

        public void Dispose()
        {
            try
            {
                // git makes its object files read only
                foreach (var file in System.IO.Directory.GetFiles(Directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                System.IO.Directory.Delete(Directory, true);
            }
            catch { }
        }
    }
}
