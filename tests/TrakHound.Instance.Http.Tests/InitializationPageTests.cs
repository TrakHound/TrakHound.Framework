// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using TrakHound.Instances;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The Initialization Page (/_init) and its Custom Page (see TrakHoundInitializationPage)
    /// </summary>
    public class InitializationPageTests : IDisposable
    {
        private readonly string _configurationPath = Path.Combine(Path.GetTempPath(), "trakhound-init-tests", Guid.NewGuid().ToString());


        public InitializationPageTests()
        {
            Directory.CreateDirectory(Path.Combine(_configurationPath, "init"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_configurationPath, true); } catch { }
        }


        [Theory]
        [InlineData("/_init/assets/logo.png", null, "/logo.png")]
        [InlineData("/_init/assets/css/site.css", null, "/css/site.css")]
        [InlineData("/_INIT/Assets/logo.png", null, "/logo.png")]
        [InlineData("/trakhound/_init/assets/logo.png", "trakhound", "/logo.png")]
        public void AssetPath_IsTheRestOfThePath(string path, string basePath, string expected)
        {
            Assert.Equal(expected, TrakHoundInitializationPage.GetAssetPath(new PathString(path), basePath)?.Value);
        }

        [Theory]
        [InlineData("/_init")]
        [InlineData("/_init/other.png")]
        [InlineData("/_initassets/logo.png")]
        [InlineData("/assets/logo.png")]
        public void AssetPath_OtherPaths_AreNotAssets(string path)
        {
            Assert.Null(TrakHoundInitializationPage.GetAssetPath(new PathString(path), null));
        }


        [Fact]
        public void CustomPage_IsRelativeToTheConfigurationProfile()
        {
            var file = TrakHoundInitializationPage.GetCustomPageFile(_configurationPath, "init");

            Assert.Equal(Path.Combine(_configurationPath, "init", "index.html"), file);
        }

        [Fact]
        public void CustomPage_CanBeAbsolute()
        {
            var folder = Path.Combine(Path.GetTempPath(), "branding");

            Assert.Equal(Path.Combine(folder, "index.html"), TrakHoundInitializationPage.GetCustomPageFile(_configurationPath, folder));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void CustomPage_NotSet_UsesTheBuiltInPage(string pagePath)
        {
            Assert.Null(TrakHoundInitializationPage.GetCustomPageFile(_configurationPath, pagePath));
            Assert.Null(TrakHoundInitializationPage.ReadCustomPage(_configurationPath, pagePath));
        }

        [Fact]
        public void CustomPage_Missing_UsesTheBuiltInPage()
        {
            Assert.Null(TrakHoundInitializationPage.ReadCustomPage(_configurationPath, "missing"));
            Assert.Null(TrakHoundInitializationPage.ReadCustomPage(_configurationPath, "init")); // The folder without an index.html
        }

        [Fact]
        public void CustomPage_IsRead()
        {
            File.WriteAllText(Path.Combine(_configurationPath, "init", "index.html"), "<h1>{{title}}</h1>");

            Assert.Equal("<h1>{{title}}</h1>", TrakHoundInitializationPage.ReadCustomPage(_configurationPath, "init"));
        }


        [Fact]
        public void Tokens_AreReplacedAndEncoded()
        {
            var tokens = TrakHoundInitializationPage.GetTokens("/app?a=1&b=<x>", TrakHoundInstanceStatus.Error);
            var html = TrakHoundInitializationPage.ReplaceTokens("<body data-status=\"{{status}}\"><img src=\"{{ assets }}/logo.png\" /><p>{{route}}</p><p>{{unknown}}</p></body>", tokens);

            Assert.Equal("<body data-status=\"Error\"><img src=\"_init/assets/logo.png\" /><p>/app?a=1&amp;b=&lt;x&gt;</p><p>{{unknown}}</p></body>", html);
        }

        [Fact]
        public void Tokens_IgnoreCase()
        {
            var tokens = TrakHoundInitializationPage.GetTokens("/", TrakHoundInstanceStatus.Starting);

            Assert.Equal("5", TrakHoundInitializationPage.ReplaceTokens("{{RetrySeconds}}", tokens));
            Assert.Equal("Starting", TrakHoundInitializationPage.ReplaceTokens("{{STATUS}}", tokens));
        }


        [Theory]
        [InlineData("/", "5;url=/")]
        [InlineData("/app/page?x=1", "5;url=/app/page?x=1")]
        [InlineData("/café", "5;url=/caf%C3%A9")] // Header values must be ASCII
        public void RefreshHeader_ReloadsTheRoute(string route, string expected)
        {
            Assert.Equal(expected, TrakHoundInitializationPage.GetRefreshHeader(route));
        }


        [Theory]
        [InlineData("/logo.png")]
        [InlineData("/css/../logo.png")]
        public void Asset_WithinTheFolder_IsServed(string assetPath)
        {
            var folder = TrakHoundInitializationPage.GetCustomPageFolder(_configurationPath, "init");
            var file = TrakHoundPackageFilesMiddleware.GetFilePath(folder, new PathString(assetPath));

            Assert.NotNull(file);
            Assert.StartsWith(folder, file);
        }

        [Theory]
        [InlineData("/../instance.config.yaml")]
        [InlineData("/../security/admins/a.yaml")]
        [InlineData("/../../.secretkey")]
        public void Asset_OutsideTheFolder_IsRejected(string assetPath)
        {
            var folder = TrakHoundInitializationPage.GetCustomPageFolder(_configurationPath, "init");

            Assert.Null(TrakHoundPackageFilesMiddleware.GetFilePath(folder, new PathString(assetPath)));
        }

        [Fact]
        public void Configuration_DefaultsToTheBuiltInPage()
        {
            Assert.Null(new TrakHoundInstanceConfiguration().InitializationPagePath);
        }
    }
}
