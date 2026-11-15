// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// --skip-existing only skips a published Version that was built from the same commit without uncommitted changes
    /// </summary>
    public class SkipExistingTests
    {
        private const string Commit = "f1e7b52cf5c47710148cc042979a23f980421a45";

        private static TrakHoundPackage CreatePackage(string commit, string dirty)
        {
            var builder = new TrakHoundPackageBuilder { Id = "TrakHound.Test.Api", Version = "0.4.9", Category = "api" };
            if (commit != null) builder.SetMetadata(TrakHoundPackage.RepositoryCommitName, commit);
            if (dirty != null) builder.SetMetadata(TrakHoundPackage.RepositoryDirtyName, dirty);
            return builder.Build();
        }

        [Fact]
        public void SameCommit_Clean_IsSkipped()
        {
            Assert.Null(CLI.Packages.Packages.GetOtherSourceReason(CreatePackage(Commit, "false"), Commit));
            Assert.Null(CLI.Packages.Packages.GetOtherSourceReason(CreatePackage(Commit.ToUpper(), "False"), Commit));
        }

        [Fact]
        public void OtherCommit_IsNotSkipped()
        {
            Assert.Contains("another commit", CLI.Packages.Packages.GetOtherSourceReason(CreatePackage("0123456789abcdef0123456789abcdef01234567", "false"), Commit));
        }

        [Theory]
        [InlineData("true")]
        [InlineData(null)]
        public void Dirty_OrUnknown_IsNotSkipped(string dirty)
        {
            Assert.Contains("uncommitted changes", CLI.Packages.Packages.GetOtherSourceReason(CreatePackage(Commit, dirty), Commit));
        }

        [Fact]
        public void NoCommit_IsNotSkipped()
        {
            Assert.Contains("no commit", CLI.Packages.Packages.GetOtherSourceReason(CreatePackage(null, "false"), Commit));
            Assert.Contains("couldn't be read", CLI.Packages.Packages.GetOtherSourceReason(null, Commit));
        }
    }
}
