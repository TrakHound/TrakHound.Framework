// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The sign in page in an Identity Provider's Package (see TrakHoundSignInPage)
    /// </summary>
    public class SignInPageTests : IDisposable
    {
        private readonly string _packageDirectory = Path.Combine(Path.GetTempPath(), "trakhound-signin-tests", Guid.NewGuid().ToString());


        public void Dispose()
        {
            try { Directory.Delete(_packageDirectory, true); } catch { }
        }


        [Theory]
        [InlineData("signin", "signin")]
        [InlineData("pages/signin", "pages/signin")]
        [InlineData(" signin/ ", "signin")]
        public void Folder_IsInThePackagesFiles(string page, string expected)
        {
            var folder = TrakHoundSignInPage.GetFolder(_packageDirectory, page);

            Assert.Equal(Path.GetFullPath(Path.Combine(_packageDirectory, "dist", expected)) + Path.DirectorySeparatorChar, folder);
        }

        [Theory]
        [InlineData(".")] // The Package's Assemblies
        [InlineData("./")]
        [InlineData("..")]
        [InlineData("../signin")]
        [InlineData("signin/../..")]
        [InlineData("C:\\Windows")]
        [InlineData("/etc")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Folder_OutsideOfThePackagesFiles_IsNotUsed(string page)
        {
            Assert.Null(TrakHoundSignInPage.GetFolder(_packageDirectory, page));
        }

        [Fact]
        public void Folder_WithBackslashes_IsTheSameOnEveryOS()
        {
            // A setting written on Windows works on Linux
            Assert.Equal(TrakHoundSignInPage.GetFolder(_packageDirectory, "pages/signin"), TrakHoundSignInPage.GetFolder(_packageDirectory, "pages\\signin"));
            Assert.NotNull(TrakHoundSignInPage.GetFolder(_packageDirectory, "pages\\signin"));
        }

        [Fact]
        public void Folder_WithoutAPackage_IsNotUsed()
        {
            Assert.Null(TrakHoundSignInPage.GetFolder(null, "signin"));
        }

        [Fact]
        public void Page_IsTheIndexFile()
        {
            var folder = TrakHoundSignInPage.GetFolder(_packageDirectory, "signin");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "index.html"), "<form></form>");

            Assert.Equal("<form></form>", TrakHoundSignInPage.ReadPage(folder));
        }

        [Fact]
        public void Page_NotFound_IsNull()
        {
            Assert.Null(TrakHoundSignInPage.ReadPage(TrakHoundSignInPage.GetFolder(_packageDirectory, "signin")));
            Assert.Null(TrakHoundSignInPage.ReadPage(null));
        }

        [Fact]
        public void Tokens_AreHtmlEncoded()
        {
            var tokens = TrakHoundSignInPage.GetTokens("/_identity/signin/a", "name", "token", "/return?a=1&b=2", "\"><script>", null, "/_identity/assets/a");

            var html = TrakHoundInitializationPage.ReplaceTokens("{{username}}|{{returnUrl}}|{{error}}", tokens);

            Assert.Equal("&quot;&gt;&lt;script&gt;|/return?a=1&amp;b=2|", html);
        }
    }
}
