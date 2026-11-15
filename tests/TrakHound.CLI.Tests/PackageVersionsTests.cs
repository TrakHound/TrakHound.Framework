// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.CLI.Packages;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// Package Versions made from the Version Prefix and the git Height
    /// </summary>
    public class PackageVersionsTests
    {
        [Theory]
        [InlineData("0.3", true)]
        [InlineData("12.40", true)]
        [InlineData("0.3.4", false)]
        [InlineData("1", false)]
        [InlineData("0.3-dev", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidPrefix(string prefix, bool expected)
        {
            Assert.Equal(expected, PackageVersions.IsValidPrefix(prefix));
        }

        [Fact]
        public void ReadPrefix_AllowsCommentsAndTrailingCommas()
        {
            Assert.Equal("0.3", PackageVersions.ReadPrefix("{\n  // the version prefix\n  \"id\": \"Test\",\n  \"Version\": \"0.3\",\n}"));
            Assert.Null(PackageVersions.ReadPrefix("{ \"id\": \"Test\" }"));
            Assert.Null(PackageVersions.ReadPrefix("not json"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("main")]
        [InlineData("master")]
        public void ExpandSuffix_DefaultBranch_HasNoBranch(string branch)
        {
            Assert.Equal($"dev.{PackageVersions.Timestamp}", PackageVersions.ExpandSuffix("dev{branch}.{timestamp}", branch));
        }

        [Fact]
        public void ExpandSuffix_OtherBranch_AddsTheBranchName()
        {
            Assert.Equal($"dev-feature-opc-ua.{PackageVersions.Timestamp}", PackageVersions.ExpandSuffix("dev{branch}.{timestamp}", "feature/opc-ua"));
            Assert.Equal("dev-fix-1-2", PackageVersions.ExpandSuffix("dev{branch}", "fix_1.2"));
        }

        [Fact]
        public void Timestamp_IsTheSameForTheWholeRun()
        {
            // Every Package in one 'th packages build' gets the same Suffix
            Assert.Equal(PackageVersions.ExpandSuffix("dev.{timestamp}", null), PackageVersions.ExpandSuffix("dev.{timestamp}", null));
            Assert.Matches("^[0-9]{14}$", PackageVersions.Timestamp);
        }


        [Fact]
        public void Height_CountsCommitsSinceThePrefixChanged()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            repo.Commit();

            Assert.Equal("0.3.1", PackageVersions.Get(path).Version);

            repo.CommitOther();
            repo.CommitOther();

            var version = PackageVersions.Get(path);
            Assert.Equal("0.3.3", version.Version);
            Assert.Equal(3, version.Height);
            Assert.Equal("0.3", version.Prefix);
        }

        [Fact]
        public void Height_CountsCommitsOutsideThePackageDirectory()
        {
            // A Package also changes when code it uses outside its directory changes (ex. a shared library)
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            repo.Commit();
            repo.CommitOther("libraries/shared.cs");

            Assert.Equal("0.3.2", PackageVersions.Get(path).Version);
        }

        [Fact]
        public void Height_StartsAgainWhenThePrefixChanges()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            repo.Commit();
            repo.CommitOther();

            repo.WritePackage("pkg", "0.4");

            // Until the change is committed, the new version line starts at 0
            Assert.Equal("0.4.0", PackageVersions.Get(path).Version);

            repo.Commit();
            Assert.Equal("0.4.1", PackageVersions.Get(path).Version);

            repo.CommitOther();
            Assert.Equal("0.4.2", PackageVersions.Get(path).Version);
        }

        [Fact]
        public void Height_IgnoresOtherChangesToTheFile()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            repo.Commit();

            repo.Write("pkg/trakhound.package.json", File.ReadAllText(path).Replace("\"api\"", "\"app\""));
            repo.Commit();

            Assert.Equal("0.3.2", PackageVersions.Get(path).Version);
        }

        [Fact]
        public void Height_FindsAPrefixChangedOnAMergedBranch()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            repo.Commit();
            repo.CommitOther();
            var main = repo.CurrentBranch;

            repo.Checkout("feature", create: true);
            repo.WritePackage("pkg", "0.4");
            repo.Commit();
            repo.CommitOther("feature.txt");

            repo.Checkout(main);
            repo.CommitOther("main.txt");
            repo.Merge("feature");

            // The commits since the prefix changed on the branch : that commit, the branch's next commit, the merge, and
            // the commit on the default branch the merge brought in (it isn't reachable from the prefix change)
            Assert.Equal("0.4.4", PackageVersions.Get(path).Version);
        }

        [Fact]
        public void Get_ReportsTheCommitAndBranch()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");
            var commit = repo.Commit();
            repo.Checkout("feature/opc-ua", create: true);

            var version = PackageVersions.Get(path);
            Assert.Equal(commit.Sha, version.Commit);
            Assert.Equal("feature/opc-ua", version.Branch);
        }

        [Fact]
        public void Get_NoPrefix_IsAnError()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", null);
            repo.Commit();

            Assert.Null(PackageVersions.Get(path));
        }

        [Fact]
        public void Get_ThreePartPrefix_IsAnError()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3.4");
            repo.Commit();

            Assert.Null(PackageVersions.Get(path));
        }

        [Fact]
        public void Get_OutsideARepository_HasNoHeight()
        {
            using var directory = new TestRepository(init: false);
            var path = directory.WritePackage("pkg", "0.3");

            var version = PackageVersions.Get(path);
            Assert.Equal("0.3.0", version.Version);
            Assert.Null(version.Commit);
        }

        [Fact]
        public void Get_RepositoryWithoutCommits_HasNoHeight()
        {
            using var repo = new TestRepository();
            var path = repo.WritePackage("pkg", "0.3");

            Assert.Equal("0.3.0", PackageVersions.Get(path).Version);
        }

        [Fact]
        public void Get_ShallowClone_IsAnError()
        {
            using var repo = new TestRepository();
            repo.WritePackage("pkg", "0.3");
            repo.Commit();
            repo.CommitOther();

            using var clone = new TestRepository(init: false);
            var cloneDirectory = Path.Combine(clone.Directory, "clone");
            if (!Git($"clone --quiet --depth 1 \"file://{repo.Directory.Replace('\\', '/')}\" \"{cloneDirectory}\"")) return; // git isn't installed

            Assert.Null(PackageVersions.Get(Path.Combine(cloneDirectory, "pkg", "trakhound.package.json")));
        }

        private static bool Git(string arguments)
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", arguments) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
