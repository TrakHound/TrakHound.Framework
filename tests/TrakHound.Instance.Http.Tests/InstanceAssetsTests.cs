// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.FileProviders;

namespace TrakHound.Instance.Http.Tests
{
    public class InstanceAssetsTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory().FullName;


        public void Dispose() => Directory.Delete(_directory, true);

        private string GetUrlFor(string contents)
        {
            File.WriteAllText(Path.Combine(_directory, "site.css"), contents);

            using var files = new PhysicalFileProvider(_directory);
            InstanceAssets.Initialize(files);

            return InstanceAssets.GetInstanceAsset("site.css");
        }

        [Fact]
        public void A_Changed_File_Gets_A_New_Url()
        {
            Assert.NotEqual(GetUrlFor(".btn { font-size: 12px; }"), GetUrlFor(".btn { font-size: 11px; }"));
        }

        [Fact]
        public void An_Unchanged_File_Keeps_Its_Url()
        {
            Assert.Equal(GetUrlFor(".btn { }"), GetUrlFor(".btn { }"));
        }

        [Fact]
        public void A_File_Outside_The_Web_Root_Uses_The_TrakHound_Version()
        {
            using var files = new PhysicalFileProvider(_directory);
            InstanceAssets.Initialize(files);

            Assert.Equal($"missing.js?v={InstanceAssets.TrakHoundVersion}", InstanceAssets.GetInstanceAsset("missing.js"));
        }
    }
}
